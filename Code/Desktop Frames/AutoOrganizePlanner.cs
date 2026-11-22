using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace Desktop_Frames
{
    internal enum OrganizeAction
    {
        Move,
        Rename,
        Overwrite,
        Skip,
        SameLocation,
        DestinationUnavailable
    }

    internal sealed record OrganizePlan(string SourcePath, OrganizeRule Rule, string? DestinationPath, OrganizeAction Action)
    {
        internal bool WillMove => Action is OrganizeAction.Move or OrganizeAction.Rename or OrganizeAction.Overwrite;
    }

    internal static class AutoOrganizePlanner
    {
        internal static IEnumerable<string> GetDesktopFiles(string desktopPath) =>
            Directory.GetFiles(desktopPath).OrderBy(path => path, StringComparer.OrdinalIgnoreCase);

        internal static OrganizeRule[] GetActiveRules(IEnumerable<OrganizeRule> rules) =>
            rules.Where(rule => rule != null && rule.IsEnabled).OrderBy(rule => rule.Priority).ToArray();

        internal static OrganizeRule? FindRule(string sourcePath, IEnumerable<OrganizeRule> activeRules)
        {
            string fileName = Path.GetFileName(sourcePath);
            string extension = Path.GetExtension(fileName).ToLowerInvariant();
            if (extension is ".lnk" or ".url" or ".crdownload" or ".part" or ".tmp") return null;

            return activeRules.FirstOrDefault(rule => DoesFileMatchRule(fileName, rule));
        }

        internal static IReadOnlyList<OrganizePlan> Preview(string desktopPath, IEnumerable<OrganizeRule> rules, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var activeRules = GetActiveRules(rules);
            var plans = new List<OrganizePlan>();
            if (activeRules.Length == 0) return plans;

            // Reserve earlier destinations so this matches a sequential desktop sweep.
            var reservedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string path in GetDesktopFiles(desktopPath))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var rule = FindRule(path, activeRules);
                if (rule == null) continue;

                var plan = PlanMove(path, rule, reservedPaths);
                plans.Add(plan);
                if (plan.WillMove && plan.DestinationPath != null) reservedPaths.Add(plan.DestinationPath);
            }

            return plans;
        }

        internal static OrganizePlan PlanMove(string sourcePath, OrganizeRule rule, ISet<string>? reservedPaths = null)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(rule.TargetFolderPath))
                    return new OrganizePlan(sourcePath, rule, null, OrganizeAction.DestinationUnavailable);

                string targetFolder = Path.GetFullPath(rule.TargetFolderPath);
                string fileName = Path.GetFileName(sourcePath);
                string destination = Path.Combine(targetFolder, fileName);

                if (File.Exists(targetFolder) || reservedPaths?.Contains(targetFolder) == true)
                    return new OrganizePlan(sourcePath, rule, destination, OrganizeAction.DestinationUnavailable);

                if (string.Equals(ResolveFileLocation(sourcePath), ResolveFileLocation(destination),
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                    return new OrganizePlan(sourcePath, rule, destination, OrganizeAction.SameLocation);

                bool destinationIsDirectory = Directory.Exists(destination);
                bool destinationIsFile = File.Exists(destination) || reservedPaths?.Contains(destination) == true;
                if (!destinationIsDirectory && !destinationIsFile)
                    return new OrganizePlan(sourcePath, rule, destination, OrganizeAction.Move);

                if (rule.ConflictAction == RuleConflictAction.Overwrite && !destinationIsDirectory)
                    return new OrganizePlan(sourcePath, rule, destination, OrganizeAction.Overwrite);
                if (rule.ConflictAction != RuleConflictAction.Rename)
                    return new OrganizePlan(sourcePath, rule, destination, OrganizeAction.Skip);

                string name = Path.GetFileNameWithoutExtension(fileName);
                string extension = Path.GetExtension(fileName);
                string alternate;
                int suffix = 1;
                do
                {
                    alternate = Path.Combine(targetFolder, $"{name} ({suffix++}){extension}");
                } while (File.Exists(alternate) || Directory.Exists(alternate) || reservedPaths?.Contains(alternate) == true);

                return new OrganizePlan(sourcePath, rule, alternate, OrganizeAction.Rename);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException or UnauthorizedAccessException)
            {
                return new OrganizePlan(sourcePath, rule, rule.TargetFolderPath, OrganizeAction.DestinationUnavailable);
            }
        }

        private static string ResolveFileLocation(string path)
        {
            var file = new FileInfo(path);
            if (file.LinkTarget != null)
                file = new FileInfo(file.ResolveLinkTarget(returnFinalTarget: true)!.FullName);

            return Path.Combine(ResolveFolderLocation(file.DirectoryName!, remainingLinks: 32), file.Name);
        }

        private static string ResolveFolderLocation(string path, int remainingLinks)
        {
            string root = Path.GetPathRoot(path)!;
            string current = root;
            foreach (string part in Path.GetRelativePath(root, path).Split(Path.DirectorySeparatorChar))
            {
                if (part == ".") continue;
                var directory = new DirectoryInfo(Path.Combine(current, part));
                if (directory.LinkTarget == null)
                {
                    current = directory.FullName;
                }
                else
                {
                    if (remainingLinks == 0) throw new IOException("Too many folder links.");
                    current = ResolveFolderLocation(directory.ResolveLinkTarget(returnFinalTarget: true)!.FullName, remainingLinks - 1);
                }
            }
            return current;
        }

        /// <summary>
        /// True when the file matches one of the patterns in a rule's extension list.
        ///
        /// The asterisk has to behave like a wildcard here. The presets rely on it:
        /// "Documents" is "*.doc*; *.pdf; *.txt; *.rtf; *.xls*; *.ppt*", where the
        /// trailing asterisk is what is meant to cover .docx and .xlsx alongside the
        /// older .doc and .xls.
        ///
        /// The previous version removed every asterisk and compared the result to the
        /// file extension for equality, so "*.doc*" became ".doc" and matched .doc but
        /// not .docx — the preset quietly ignored every modern Office file.
        ///
        /// That exact comparison is kept as a first check, so any list that worked
        /// before still works, including entries someone typed without a wildcard.
        /// </summary>
        private static bool MatchesAnyExtensionPattern(string fileName, string patterns)
        {
            string name = Path.GetFileName(fileName).ToLowerInvariant();
            string ext = Path.GetExtension(fileName).ToLowerInvariant();

            foreach (string raw in patterns.Split(new[] { ';', ',', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string pattern = raw.Trim().ToLowerInvariant();
                if (pattern.Length == 0) continue;

                // Old behaviour, preserved: "*.pdf" stripped of asterisks is ".pdf".
                if (pattern.Replace("*", "") == ext) return true;

                // New: the asterisk now stands for anything, on the whole file name.
                if (pattern.IndexOf('*') >= 0)
                {
                    string expression = "^" + System.Text.RegularExpressions.Regex.Escape(pattern).Replace("\\*", ".*") + "$";
                    if (System.Text.RegularExpressions.Regex.IsMatch(name, expression)) return true;
                }
            }

            return false;
        }

        private static bool DoesFileMatchRule(string fileName, OrganizeRule rule)
        {
            // 1. Check Extensions
            if (!string.IsNullOrWhiteSpace(rule.Extensions) && !rule.Extensions.Contains("*.*") && rule.Extensions.Trim() != "*")
            {
                // If the file matches none of the patterns, reject it
                if (!MatchesAnyExtensionPattern(fileName, rule.Extensions)) return false;
            }

            // 2. Check Name Contains (AND condition)
            if (!string.IsNullOrWhiteSpace(rule.NameContains))
            {
                // If the filename does NOT contain the required text, reject it
                if (fileName.IndexOf(rule.NameContains, StringComparison.OrdinalIgnoreCase) == -1)
                    return false;
            }

            // If it survived both filters, it is a perfect match!
            return true;
        }
    }
}
