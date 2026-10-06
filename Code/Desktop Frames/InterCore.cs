using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Media;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Desktop_Frames
{
    // InterCore system for Desktop Frames - handles special interactive features and animations.
    public static class InterCore
    {
        #region Private Fields

        private static bool _isDanceActive = false;
        private static bool _isGravityActive = false;
        private static readonly Dictionary<StackPanel, Point> _originalIconPositions = new Dictionary<StackPanel, Point>();
        private static Window _sparkleOverlay;

        // Legendary Mode Fields
        private static readonly Dictionary<string, Storyboard> _legendaryEffects = new Dictionary<string, Storyboard>();
        private static readonly Dictionary<string, Brush> _originalBorders = new Dictionary<string, Brush>();
        private static readonly Dictionary<string, Thickness> _originalBorderThicknesses = new Dictionary<string, Thickness>();
        private static readonly Dictionary<string, Effect> _originalEffects = new Dictionary<string, Effect>();

        // --- NEW: Registry Listener Fields ---
        private static DispatcherTimer _registryMonitor;
        private static string _lastTriggerValue;

        // --- NEW: Seasonal Easter Egg Fields ---
        private static DispatcherTimer _winterTimer;
        private static bool _isWinterActive = false;
        private static readonly Dictionary<Window, WinterContext> _winterContexts = new Dictionary<Window, WinterContext>();
        private static readonly List<Snowflake> _snowflakes = new List<Snowflake>();

        // New Year's mode flag. Set once during Initialize when today's date
        // falls in the Jan 1–2 window. It does not switch at midnight on a
        // running instance; the user would need to restart, which is the
        // same constraint every other seasonal theme operates under. The
        // flag is read in two places: InjectSnowCanvas (to add the horizon
        // glow and year watermark and to warm the star colour) and the
        // physics loop (to drive firework and confetti schedules).
        private static bool _isNewYearActive = false;

        // Fireworks and confetti are tracked in global lists, matching the
        // pattern used by _snowflakes. Each particle knows its parent window,
        // so a single physics pass handles every frame's particles together.
        // The lists are pruned in the same pass that ticks them.
        private static readonly List<FireworkComet> _fireworkComets = new List<FireworkComet>();
        private static readonly List<FireworkSpark> _fireworkSparks = new List<FireworkSpark>();
        private static readonly List<Confetti> _confetti = new List<Confetti>();
        private static readonly List<Glitter> _glitter = new List<Glitter>();

        private static DispatcherTimer _halloweenTimer;
        private static bool _isHalloweenActive = false;
        private static readonly Dictionary<Window, HalloweenContext> _halloweenContexts = new Dictionary<Window, HalloweenContext>();
        private static readonly List<Ember> _embers = new List<Ember>();

        private static readonly Random _random = new Random();
        private static int _winterTickCounter = 0;
        private static int _halloweenTickCounter = 0;

        // Palette for the pumpkin face glow. Weighted so the warm candlelight
        // is the default and the spookier hues are a rare treat rather than a
        // constant strobe. When the picker lands on the same non-default hue
        // twice in a row, it drifts back to candlelight, so the effect always
        // reads as "an orange pumpkin that briefly flickers green", never as
        // "a colour-cycling ornament".
        private static readonly (Color Color, double Weight)[] PumpkinFacePalette = new[]
        {
            (Color.FromRgb(255, 230, 100), 0.55),  // Warm candlelight — the default
            (Color.FromRgb(255, 190, 60), 0.20),   // Deeper gold
            (Color.FromRgb(255, 140, 40), 0.15),   // Orange fire
            (Color.FromRgb(180, 255, 120), 0.05),  // Rare ghost-green
            (Color.FromRgb(160, 200, 255), 0.05)   // Very rare pale-blue
        };

        private static int PickPumpkinColorIndex(int currentIndex)
        {
            double r = _random.NextDouble();
            double cumulative = 0;
            int pick = 0;
            for (int i = 0; i < PumpkinFacePalette.Length; i++)
            {
                cumulative += PumpkinFacePalette[i].Weight;
                if (r <= cumulative) { pick = i; break; }
            }
            // Never repeat a non-default hue twice in a row — drift home.
            if (pick == currentIndex && pick != 0) pick = 0;
            return pick;
        }

        private class WinterContext
        {
            public Canvas Canvas { get; set; }
            public Polygon Ground { get; set; }
            public double[] Accumulation { get; set; }
            public double[] MaxAccumulation { get; set; }
            public int Segments { get; set; }

            // -----------------------------------------------------------------
            // Sky layer. Sits on its own canvas behind the frame's own
            // content, so the gradient tints the frame background toward
            // night without ever darkening the icons or the title bar.
            // -----------------------------------------------------------------
            public Rectangle SkyGradient { get; set; }

            // Stars: tiny cool-white ellipses scattered across the top of
            // the frame, twinkling on independent phases. Held as parallel
            // arrays (visuals and phases) so the physics loop can update
            // every star with a single indexed iteration, no dictionary
            // lookups on the hot path.
            public List<Ellipse> Stars { get; set; }
            public double[] StarPhases { get; set; }
            public double[] StarBaseOpacity { get; set; }

            // Four-point glints for the brightest stars. Parallel to Stars:
            // entry [i] is the glint belonging to star [i], or null if that
            // star is too dim to deserve one. Only stars with a base
            // opacity above the threshold get a glint — a field where every
            // star has rays reads as busy and mechanical, whereas a field
            // with three or four shining stars reads as a night sky.
            public List<Shape> StarGlints { get; set; }

            // -----------------------------------------------------------------
            // The moon, its halo, its atmospheric haze, and the mist wisps
            // that cross it. Held as a single Canvas so the physics loop can
            // reposition the entire group with one SetLeft call, and so the
            // halo can extend past the frame's right edge without any
            // per-element bookkeeping.
            // -----------------------------------------------------------------
            public Canvas MoonCanvas { get; set; }

            // Outer atmospheric haze — the wide, soft, warm gold glow that
            // makes the moon read as "seen through a kilometre of cold air".
            // Held separately from the disc so the physics loop can pulse its
            // opacity on a slow cycle, which is what gives the moon a living
            // presence rather than a static stamped look.
            public Ellipse MoonHaze { get; set; }

            // The mist wisps' shared drift transform. Both wisps use the
            // same transform so a shift applied to it moves the mist as one
            // body rather than as two independent rectangles.
            public TranslateTransform MoonCloudDrift { get; set; }

            // Absolute time (matching DateTime.Now.TimeOfDay) at which the
            // next huge flake should spawn. Each frame carries its own
            // value, initialised to a random offset when the frame is
            // injected, so on a multi-frame desktop the huge flakes
            // arrive staggered rather than simultaneously. Using absolute
            // time rather than a tick counter keeps the schedule immune
            // to the physics loop running at slightly different rates on
            // different frames (which it does, because DispatcherTimers
            // are not perfectly synchronised).
            public double NextHugeFlakeTime { get; set; }


            // -----------------------------------------------------------------
            // NEW YEAR'S FIELDS
            //
            // All of these are only populated when _isNewYearActive was
            // true at the frame's injection time. If the theme is winter
            // (not New Year's), these remain null and the physics loop
            // guards its New Year-specific work on that.
            // -----------------------------------------------------------------

            // The warm horizon glow. A wide rectangle at the bottom of the
            // sky canvas with a vertical gradient from amber at its base to
            // transparent at its top, sized to the frame in the physics
            // loop. Its role is to reposition the scene from "empty winter
            // field" to "somewhere near a celebration", which is what makes
            // the fireworks feel motivated.
            public Rectangle HorizonGlow { get; set; }

            // The year watermark. A large gold TextBlock centered in the
            // frame. Deliberately faint: the eye should notice it before
            // it reads it.
            public TextBlock YearWatermark { get; set; }

            // The transparent canvas that hosts glitter sparkles. Added to
            // the visual tree AFTER the year watermark, so sparkles draw
            // over the digits. IsHitTestVisible is false, so clicks pass
            // through to the icons below, exactly as they pass through
            // the snow canvas.
            public Canvas GlitterCanvas { get; set; }

            // Absolute time of the next glitter batch. Absolute time rather
            // than a tick counter, so the schedule is immune to any jitter
            // in the dispatcher timer's actual firing rate.
            public double NextGlitterTime { get; set; }

            // Schedule for the next firework launch. Absolute time, like
            // NextHugeFlakeTime, so different frames never synchronise.
            // The schedule is: launch first at a random point within the
            // first 40 seconds, then every 25–60 seconds.
            public double NextFireworkTime { get; set; }

            // Schedule for the next confetti spawn. Confetti is spawned one
            // piece at a time on a slow cadence, with a cap of four active
            // per frame. This keeps the field sparse and never crowds the
            // icons — the goal is a festive hint, not a blizzard.
            public double NextConfettiTime { get; set; }


            // Per-star twinkle frequency. If every star pulsed at the same
            // rate, the field would breathe as a single unit — the eye
            // picks that up immediately and reads it as "something is
            // animating the sky". Giving each star its own frequency
            // breaks that synchronisation: some stars pulse fast, some
            // slow, none in step. The effect reads as individual distant
            // suns, which is what the eye expects from a real sky.
            public double[] StarSpeeds { get; set; }
            // -----------------------------------------------------------------
            // Snow-surface detail polygons.
            //
            // CrestLine: a thin, sharp, bright band sitting exactly on top
            // of the ground polygon's top edge. The parent's soft blur
            // makes the snow read as a soft blanket; this crest adds the
            // defined upper contour that turns it into a snow surface.
            //
            // CrestShadow: a thin, cool-blue band just beneath the crest.
            // This is the classic painter's trick for giving snow volume:
            // the crest is warm (lit by the moon), the recessed side is
            // cool (in shadow). Without it the ground is a flat white mask.
            // -----------------------------------------------------------------
            public Polygon CrestLine { get; set; }
            public Polygon CrestShadow { get; set; }
        }

        private class HalloweenContext
        {
            public Canvas Canvas { get; set; }

            // Sky gradient sits in its own canvas *behind* the frame's own
            // content, so it tints the frame background toward night without
            // ever darkening the icons or the title bar. The rectangle is
            // resized on every tick to track frame resizes.
            public Rectangle SkyGradient { get; set; }

            public Rectangle BackMist { get; set; }

            // Mid mist: a new middle layer that sits between the back mist and
            // the pumpkins, and drifts horizontally at a slower rate than the
            // front mist. Its presence is what turns the effect from a
            // two-plane diorama into something with genuine volume.
            public Polygon MidMist { get; set; }

            public Polygon FrontMist { get; set; }

            public UIElement BigPumpkin { get; set; }   // The primary pumpkin
            public UIElement SmallPumpkin { get; set; } // The smaller companion

            // Face-glow references, captured at injection time so the physics
            // loop can flicker them and occasionally swap the hue without ever
            // walking the visual tree on a tick.
            public DropShadowEffect BigPumpkinFaceGlow { get; set; }
            public DropShadowEffect SmallPumpkinFaceGlow { get; set; }

            // Independent flicker phases so the two pumpkins never pulse in
            // lockstep. Randomised per frame.
            public double BigPumpkinFlickerPhase { get; set; }
            public double SmallPumpkinFlickerPhase { get; set; }

            // Absolute time (seconds, matching DateTime.Now.TimeOfDay) at which
            // the next hue swap should occur. Absolute time keeps swaps
            // independent of tick-rate drift.
            public double BigPumpkinNextColorChange { get; set; }
            public double SmallPumpkinNextColorChange { get; set; }
            public int BigPumpkinColorIndex { get; set; }
            public int SmallPumpkinColorIndex { get; set; }

            public double[] MistPhases { get; set; }
            public double[] MidMistPhases { get; set; }
            public int Segments { get; set; }
            public int MidMistSegments { get; set; }

            // -----------------------------------------------------------------
            // Zigzag ridge layers.
            //
            // Each ridge is a thin polygon whose top edge follows a
            // down-scaled version of its parent mist's contour. The ridges
            // share the parent mist's phase array, indexed proportionally,
            // so a peak in the parent mist always produces a matching peak
            // in its ridge. That alignment is what makes the layers read as
            // one body of fog with a defined upper contour, rather than as
            // two unrelated bands that happen to overlap.
            //
            // The thicker parent mist provides body and colour; the thin
            // ridge sits just above it and adds the "crest line" that turns
            // a soft blob into a recognisable atmospheric profile. This is
            // the standard cinematic convention for layered fog: opacity
            // bands separated by thin, high-contrast edges.
            // -----------------------------------------------------------------
            public Polygon FrontRidge { get; set; }
            public Polygon MidRidge { get; set; }
            public Polygon HighRidge { get; set; }

            // High ridge has no parent mist, so it needs its own phases.
            public double[] HighRidgePhases { get; set; }

            // The moon, its halo, and the clouds that cross it. Held as a
            // single Canvas so the physics loop can reposition the entire
            // group with one SetLeft call, and so the halo can extend beyond
            // the frame's right edge without any per-element bookkeeping.
            public Canvas MoonCanvas { get; set; }

            // Cloud RenderTransform targets, driven by the physics loop for a
            // slow horizontal drift. Real clouds through mist move visibly
            // even when the scene is otherwise still, and this is what keeps
            // the sky from reading as a static image pasted behind the frame.
            public TranslateTransform MoonCloudDrift { get; set; }
        }

        // Snowflake visual shapes. Chosen per depth so distant flakes are
        // simple circles (they are too small to resolve detail) and near
        // flakes carry a recognisable crystalline silhouette.
        private enum FlakeShape { Circle, Hexagon, Star }

        // Three depth bands. Each band gets its own cluster of visual
        // parameters. Real snowfalls read as "layers of snow falling through
        // each other", not as a single plane of identical particles, so the
        // visual difference between a far flake and a near flake is the single
        // biggest quality lever in this effect.
        private enum FlakeDepth { Far, Mid, Near }

        /// <summary>
        /// A firework in its rising phase. The comet is a bright point with
        /// a soft glow, travelling from below the frame's bottom edge
        /// upward to a predetermined apex. When it reaches the apex, it is
        /// removed and replaced by a burst of sparks (see
        /// BurstFirework). Treating rise and burst as two separate objects
        /// is what keeps the two phases independently tunable: the comet's
        /// speed, glow, and colour are its own; the sparks' count, spread,
        /// and palette belong to the burst.
        ///
        /// Each comet carries the burst palette it will use when it bursts.
        /// The palette is chosen at launch time, not at burst time, so the
        /// comet's own colour can match the dominant burst colour — a
        /// small touch that makes the sequence read as one event rather
        /// than two coincident ones.
        /// </summary>
        private class FireworkComet
        {
            public Ellipse Visual { get; set; }
            public NonActivatingWindow ParentWindow { get; set; }
            public double BaseX { get; set; }
            public double Y { get; set; }
            public double ApexY { get; set; }
            public double Speed { get; set; }
            public Color BurstColor { get; set; }
            public Color BurstAccent { get; set; }
            public double SwayPhase { get; set; }
            public double SwayAmplitude { get; set; }
        }

        /// <summary>
        /// A single spark after a firework burst. Sparks obey simple
        /// gravity: they start with an outward velocity and decelerate
        /// vertically as if falling, which is what makes a burst read as
        /// "exploding at an apex" rather than "expanding in place". Each
        /// spark has its own lifespan; opacity fades linearly from 1 to 0
        /// over that lifespan.
        /// </summary>
        private class FireworkSpark
        {
            public Ellipse Visual { get; set; }
            public NonActivatingWindow ParentWindow { get; set; }
            public double X { get; set; }
            public double Y { get; set; }
            public double VX { get; set; }
            public double VY { get; set; }
            public double Life { get; set; }
            public double MaxLife { get; set; }
        }

        /// <summary>
        /// A single piece of confetti. Modelled as a small rectangle that
        /// falls slowly, sways horizontally, and tumbles around its own
        /// axis. The tumble is what distinguishes confetti from snow at a
        /// glance: two rectangles at the same size and colour read as
        /// "snow with a bug" until one of them rotates.
        /// </summary>
        /// <summary>
        /// A single glitter sparkle on the year watermark. Very short-lived
        /// and very bright — the opposite of the year text itself, which is
        /// faint and permanent. The sparkle's whole visual purpose is to
        /// catch the eye for a fraction of a second and then vanish, which
        /// is what reads as "glitter catching light".
        ///
        /// Sparkles are drawn on the dedicated GlitterCanvas, which sits
        /// above the year in the visual tree. Nothing about them touches
        /// the year TextBlock itself — the text stays exactly as the user
        /// saw it; the sparkles are purely an overlay.
        /// </summary>
        private class Glitter
        {
            public Ellipse Visual { get; set; }
            public NonActivatingWindow ParentWindow { get; set; }
            public double X { get; set; }
            public double Y { get; set; }
            public double Life { get; set; }
            public double MaxLife { get; set; }
            public double PeakOpacity { get; set; }
        }

        private class Confetti
        {
            public Rectangle Visual { get; set; }
            public NonActivatingWindow ParentWindow { get; set; }
            public double BaseX { get; set; }
            public double Y { get; set; }
            public double Speed { get; set; }
            public double DriftOffset { get; set; }
            public double DriftSpeed { get; set; }
            public double RotationPhase { get; set; }
            public double RotationSpeed { get; set; }
            public double Life { get; set; }
            public double MaxLife { get; set; }
        }

        private class Snowflake
        {
            // Base Shape so that a flake can be any Shape-derived visual —
            // Ellipse for the circle, Polygon for hexagon and star. All uses
            // in this file go through members common to Shape (Width, Height,
            // Opacity, RenderTransform), so no call sites need to change.
            public Shape Visual { get; set; }
            public NonActivatingWindow ParentWindow { get; set; }
            public double BaseX { get; set; }
            public double Y { get; set; }
            public double Speed { get; set; }
            public double Size { get; set; }
            public double DriftOffset { get; set; }
            public double DriftSpeed { get; set; }
            public bool IsMelting { get; set; }
            public double MeltRate { get; set; }

            // Depth band, chosen at spawn. Determines size, opacity, fall
            // speed, sway amplitude, and shape, via a single lookup table in
            // RespawnSnowflake. Persisted between respawns so a flake that
            // recycles stays in the same depth band — otherwise the near
            // layer would visually churn as flakes hopped between layers on
            // every melt cycle.
            public FlakeDepth Depth { get; set; }

            // Slow rotation as the flake falls. Real flakes tumble; the sway
            // alone only sells horizontal drift. Rotation is applied in the
            // physics loop as an angle update on a RotateTransform attached
            // to the flake's RenderTransform.
            public double RotationPhase { get; set; }
            public double RotationSpeed { get; set; }

            // One-shot huge flakes. Unlike regular flakes, huge flakes do
            // not respawn when they melt — they are removed from the active
            // list entirely and a fresh one is scheduled by the per-frame
            // timer in WinterContext. This is what keeps them feeling
            // "occasional" rather than "the field has a size range".
            //
            // Huge flakes use the same physics loop as regular flakes (fall,
            // sway, rotate) but bypass the accumulation logic and pick
            // their own visual parameters in RespawnSnowflake.
            public bool IsHuge { get; set; }
        }

        private class Ember
        {
            public Ellipse Core { get; set; } // The crisp spark
            public Ellipse Glow { get; set; } // The massive light scatter inside the fog
            public NonActivatingWindow ParentWindow { get; set; }
            public double BaseX { get; set; }
            public double Y { get; set; }
            public double Speed { get; set; }
            public double Size { get; set; }
            public double GlowSize { get; set; } // Added to track dynamic volume based on depth
            public double DriftOffset { get; set; }
            public double DriftSpeed { get; set; }
            public int RespawnDelayTicks { get; set; }
            public double MaxOpacity { get; set; }
        }
        // ------------------------------------

        #endregion

        #region Public Methods

        // Initializes the InterCore system - call this during application startup
        public static void Initialize()
        {
            try
            {
                // REGISTER HOTKEYS from GlobalHotkeyManager
                GlobalHotkeyManager.DancePartyTriggered += (s, e) => ActivateDanceParty();
                GlobalHotkeyManager.GravityDropTriggered += (s, e) => ActivateGravityDrop();

                // --- NEW: Start Registry Listener ---
                _registryMonitor = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000) };
                _registryMonitor.Tick += CheckRegistryTrigger;
                _registryMonitor.Start();

                // Check for Seasonal Events
                var now = DateTime.Now;
                if (now.Month == 12 && now.Day >= 22 && now.Day <= 29)
                {
                    ActivateWinterTheme();
                }
                else if (now.Month == 1 && now.Day >= 1 && now.Day <= 2)
                {
                    // New Year's occupies the two days that follow the
                    // Christmas window with no gap: on the 31st the winter
                    // effect runs, on the 1st New Year's takes over. The
                    // user sees a continuous scene that transitions only in
                    // its particle content, not in its stage.
                    ActivateNewYearTheme();
                }
                else if ((now.Month == 10 && now.Day >= 29) || (now.Month == 11 && now.Day <= 2))
                {
                    ActivateHalloweenTheme();
                }

                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.UI, "InterCore system initialized and subscribed to global hotkeys");
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI, $"InterCore: Error during initialization: {ex.Message}");
            }
        }

        // Cleans up any active effects - call this during application shutdown
        public static void Cleanup()
        {
            try
            {
                _registryMonitor?.Stop(); // Stop listener
                _winterTimer?.Stop(); // Stop winter physics loop
                _halloweenTimer?.Stop(); // Stop halloween physics loop
                _sparkleOverlay?.Close();
                _originalIconPositions.Clear();

                // Unsubscribe to prevent leaks
                GlobalHotkeyManager.DancePartyTriggered -= (s, e) => ActivateDanceParty();
                GlobalHotkeyManager.GravityDropTriggered -= (s, e) => ActivateGravityDrop();

                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.UI, "InterCore system cleaned up");
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI, $"InterCore: Error during cleanup: {ex.Message}");
            }
        }

        private static void CheckRegistryTrigger(object sender, EventArgs e)
        {
            string currentValue = RegistryHelper.CheckForTrigger();

            // 1. If registry is empty, reset memory so we can accept new commands
            if (string.IsNullOrEmpty(currentValue))
            {
                _lastTriggerValue = null;
                return;
            }

            // 2. Debounce: If exactly the same string as last time, ignore it
            if (currentValue == _lastTriggerValue)
                return;

            _lastTriggerValue = currentValue; // Mark as processed

            // --- THE FIX IS HERE ---
            // You are sending "CMD_DRAW|{GUID}", so strict "==" will FAIL.
            // You MUST use .StartsWith to detect the command portion.
            if (currentValue.StartsWith("CMD_DRAW"))
            {
                // Correct Command -> Trigger Draw Mode
                Application.Current.Dispatcher.Invoke(() => Framemanager.StartDrawMode());
                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.General, $"Remote Command Received: Draw Mode ({currentValue})");
            }
            else
            {
                // Timestamp or Unknown -> Trigger Wake Up (Blockage)
                Application.Current.Dispatcher.InvokeAsync(async () =>
                {
                    if (Framemanager._areFramesAutoHidden)
                    {
                        Framemanager.WakeUpFrames();

                        // --- BUG FIX: The Animation Race Condition ---
                        // We must wait for the WakeUp fade-in to finish completely before 
                        // the sweep samples the window opacity. Otherwise, it captures Opacity=0 
                        // and permanently hides them after the sweep completes.
                        await System.Threading.Tasks.Task.Delay(600);
                    }

                    ActivateLighthouseSweep();
                });

                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.General, $"Remote Trigger Received: Wake Up ({currentValue})");
            }

            // 3. Cleanup registry immediately
            RegistryHelper.DeleteTrigger();
        }

        // Processes frame title changes for special triggers (e.g. "limbo666")
        public static string ProcessTitleChange(dynamic frame, string newTitle, string originalTitle)
        {
            try
            {
                // 1. Limbo666 Easter Egg (One-time trigger, Reverts title)
                if (string.Equals(newTitle, "limbo666", StringComparison.OrdinalIgnoreCase))
                {
                    LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.UI, "InterCore: limbo666 trigger activated");
                    ActivateSparkleEffect();
                    return originalTitle; // Revert
                }

                // 2. The "Nikos" Legendary Mode (Persistent, Keeps title)
                bool isLegendary = string.Equals(newTitle, "Nikos", StringComparison.OrdinalIgnoreCase) ||
                                   string.Equals(newTitle, "Nikos Georgousis", StringComparison.OrdinalIgnoreCase) ||
                                   (!string.IsNullOrEmpty(newTitle) && newTitle.Contains(">:"));

                // We need the window to apply effects. Find it by ID.
                string frameId = frame.Id?.ToString();
                var win = Application.Current.Windows.OfType<NonActivatingWindow>().FirstOrDefault(w => w.Tag?.ToString() == frameId);

                if (win != null)
                {
                    if (isLegendary)
                    {
                        ActivateLegendaryMode(win, frameId);
                    }
                    else
                    {
                        // If it WAS legendary but user changed name to "Work", deactivate it
                        DeactivateLegendaryMode(win, frameId);
                    }
                }

                return newTitle; // Keep the new name (e.g. "Nikos")
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI, $"InterCore: Error processing title change: {ex.Message}");
                return newTitle;
            }
        }

        #region Legendary Mode (Nikos)

        private static void ActivateLegendaryMode(Window win, string frameId)
        {
            if (_legendaryEffects.ContainsKey(frameId)) return; // Already active

            try
            {
                var border = win.Content as Border;
                if (border == null) return;

                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.UI, $"InterCore: Activating Legendary Mode for {frameId}");

                // 1. Save Original State
                _originalBorders[frameId] = border.BorderBrush;
                _originalBorderThicknesses[frameId] = border.BorderThickness;
                _originalEffects[frameId] = win.Effect;

                // 2. Create "Mary-Go-Round" Gradient Brush
                // A vibrant rainbow gradient
                var rainbowBrush = new LinearGradientBrush
                {
                    StartPoint = new Point(0, 0),
                    EndPoint = new Point(1, 1),
                    GradientStops = new GradientStopCollection
                    {
                        //new GradientStop(Colors.Red, 0.0),
                        //new GradientStop(Colors.Gold, 0.2),
                        //new GradientStop(Colors.Lime, 0.4),
                        //new GradientStop(Colors.Cyan, 0.6),
                        //new GradientStop(Colors.Magenta, 0.8),
                        //new GradientStop(Colors.Red, 1.0)

                        new GradientStop(Colors.Yellow, 0.0),
                        new GradientStop(Colors.Purple, 0.2),
                        new GradientStop(Colors.YellowGreen, 0.4),
                        new GradientStop(Colors.Yellow, 0.6),
                        new GradientStop(Colors.Purple, 0.8),
                        new GradientStop(Colors.YellowGreen, 1.0)

                    },
                    RelativeTransform = new RotateTransform(0, 0.5, 0.5) // Rotate around center
                };

                // 3. Apply New Visuals
                border.BorderBrush = rainbowBrush;
                border.BorderThickness = new Thickness(4); // Make it thick enough to see

                // Add a glowing outer effect
                win.Effect = new DropShadowEffect
                {
                    Color = Colors.Cyan,
                    BlurRadius = 20,
                    ShadowDepth = 0,
                    Opacity = 0.8
                };

                // 4. Create Animation (Infinite Rotation)
                DoubleAnimation rotateAnim = new DoubleAnimation
                {
                    From = 0,
                    To = 360,
                    Duration = TimeSpan.FromSeconds(3), // Speed of rotation
                    RepeatBehavior = RepeatBehavior.Forever
                };

                // Apply animation to the brush's transform
                rainbowBrush.RelativeTransform.BeginAnimation(RotateTransform.AngleProperty, rotateAnim);

                _legendaryEffects[frameId] = new Storyboard();
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI, $"InterCore: Error activating Legendary Mode: {ex.Message}");
            }
        }

        private static void DeactivateLegendaryMode(Window win, string frameId)
        {
            if (!_legendaryEffects.ContainsKey(frameId)) return; // Not active

            try
            {
                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.UI, $"InterCore: Deactivating Legendary Mode for {frameId}");

                var border = win.Content as Border;
                if (border != null)
                {
                    // Restore Border
                    if (_originalBorders.ContainsKey(frameId)) border.BorderBrush = _originalBorders[frameId];
                    if (_originalBorderThicknesses.ContainsKey(frameId)) border.BorderThickness = _originalBorderThicknesses[frameId];
                }

                // Restore Effect
                if (_originalEffects.ContainsKey(frameId)) win.Effect = _originalEffects[frameId];
                else win.Effect = null;

                // Clean up memory
                _legendaryEffects.Remove(frameId);
                _originalBorders.Remove(frameId);
                _originalBorderThicknesses.Remove(frameId);
                _originalEffects.Remove(frameId);
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI, $"InterCore: Error deactivating Legendary Mode: {ex.Message}");
            }
        }

        #endregion

        #endregion

        #region Private Methods - Dance Party (Ctrl+Alt+D)

        private static void ActivateDanceParty()
        {
            if (_isDanceActive) return;

            LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.UI, "InterCore: Dance Party activated!");
            _isDanceActive = true;

            try
            {
                // Play MIDI music
                PlayHappyTune2();

				// Get all frame icons
				var frameWindows = Application.Current.Windows.OfType<NonActivatingWindow>();
                var allIcons = new List<StackPanel>();

                foreach (var window in frameWindows)
                {
                    var wrapPanel = FrameUtilities.FindWrapPanel(window);
                    if (wrapPanel != null)
                    {
                        allIcons.AddRange(wrapPanel.Children.OfType<StackPanel>());
                    }
                }

                // Create bounce animation
                foreach (var icon in allIcons)
                {
                    var bounceAnimation = new DoubleAnimationUsingKeyFrames();
                    var easing = new BounceEase { EasingMode = EasingMode.EaseOut };

                    bounceAnimation.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0))));
                    bounceAnimation.KeyFrames.Add(new EasingDoubleKeyFrame(-20, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.3))) { EasingFunction = easing });
                    bounceAnimation.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.6))) { EasingFunction = easing });

                    bounceAnimation.RepeatBehavior = new RepeatBehavior(TimeSpan.FromSeconds(10));

                    if (icon.RenderTransform == null || icon.RenderTransform == Transform.Identity)
                        icon.RenderTransform = new TranslateTransform();

                    var transform = icon.RenderTransform as TranslateTransform ?? new TranslateTransform();
                    icon.RenderTransform = transform;

                    transform.BeginAnimation(TranslateTransform.YProperty, bounceAnimation);
                }

                // Reset flag
                var resetTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
                resetTimer.Tick += (s, e) =>
                {
                    _isDanceActive = false;
                    resetTimer.Stop();
                };
                resetTimer.Start();
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI, $"InterCore: Error in Dance Party: {ex.Message}");
                _isDanceActive = false;
            }
        }

        private static void PlayHappyTune2()
        {
            if (SettingsManager.EnableSounds == false) return;

            try
            {
                var midiOut = new NAudio.Midi.MidiOut(0);
                midiOut.Send(NAudio.Midi.MidiMessage.ChangePatch(12, 1).RawData);

                var thread = new Thread(() =>
                {
                    int[][] chords = { new[] { 60, 64, 67 }, new[] { 67, 71, 74 }, new[] { 69, 72, 76 }, new[] { 65, 69, 72 } };

                    for (int i = 0; i < 14; i++)
                    {
                        foreach (var note in chords[i % chords.Length])
                            midiOut.Send(NAudio.Midi.MidiMessage.StartNote(note, 90, 1).RawData);

                        Thread.Sleep(300);

                        // Staccato rhythm
                        for (int j = 0; j < 4; j++)
                        {
                            int rootNote = chords[i % chords.Length][0];
                            midiOut.Send(NAudio.Midi.MidiMessage.StopNote(rootNote, 0, 1).RawData);
                            midiOut.Send(NAudio.Midi.MidiMessage.StartNote(rootNote + (j % 2 == 0 ? 0 : 7), 110, 1).RawData);
                            Thread.Sleep(100);
                        }
                        foreach (var note in chords[i % chords.Length])
                            midiOut.Send(NAudio.Midi.MidiMessage.StopNote(note, 0, 1).RawData);
                    }
                    midiOut.Dispose();
                });

                thread.IsBackground = true;
                thread.Start();
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.UI, $"InterCore: Audio error: {ex.Message}");
            }
        }

        #endregion

        #region Private Methods - Gravity Drop (Ctrl+Shift+G)

        private static void ActivateGravityDrop()
        {
            if (_isGravityActive) return;

            LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.UI, "InterCore: Gravity Drop activated!");
            _isGravityActive = true;

            try
            {
                var frameWindows = Application.Current.Windows.OfType<NonActivatingWindow>();
                var allIcons = new List<StackPanel>();
                _originalIconPositions.Clear();

                foreach (var window in frameWindows)
                {
                    var wrapPanel = FrameUtilities.FindWrapPanel(window);
                    if (wrapPanel != null)
                        allIcons.AddRange(wrapPanel.Children.OfType<StackPanel>());
                }

                foreach (var icon in allIcons)
                {
                    var random = new Random();
                    var fallDistance = 500 + random.Next(100);
                    var fallDuration = 1.5 + random.NextDouble() * 0.5;

                    var fallAnimation = new DoubleAnimation
                    {
                        From = 0,
                        To = fallDistance,
                        Duration = TimeSpan.FromSeconds(fallDuration),
                        EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
                    };

                    var bounceAnimation = new DoubleAnimation
                    {
                        From = fallDistance,
                        To = 0,
                        Duration = TimeSpan.FromSeconds(0.8),
                        EasingFunction = new BounceEase { EasingMode = EasingMode.EaseOut, Bounces = 3 },
                        BeginTime = TimeSpan.FromSeconds(fallDuration)
                    };

                    if (icon.RenderTransform == null || icon.RenderTransform == Transform.Identity)
                        icon.RenderTransform = new TranslateTransform();

                    var transform = icon.RenderTransform as TranslateTransform ?? new TranslateTransform();
                    icon.RenderTransform = transform;

                    transform.BeginAnimation(TranslateTransform.YProperty, fallAnimation);

                    var bounceTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(fallDuration) };
                    bounceTimer.Tick += (s, e) =>
                    {
                        transform.BeginAnimation(TranslateTransform.YProperty, bounceAnimation);
                        bounceTimer.Stop();
                    };
                    bounceTimer.Start();
                }

                var resetTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
                resetTimer.Tick += (s, e) =>
                {
                    _isGravityActive = false;
                    _originalIconPositions.Clear();
                    resetTimer.Stop();
                };
                resetTimer.Start();
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI, $"InterCore: Error in Gravity Drop: {ex.Message}");
                _isGravityActive = false;
            }
        }

        #endregion

        #region Private Methods - Lighthouse Sweep (Single Instance Effect)

        private static bool _isLighthouseSweepActive = false;

        public static void ActivateLighthouseSweep()
        {
            if (_isLighthouseSweepActive)
            {
                LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.UI,
                    "InterCore: Lighthouse sweep already active, skipping");
                return;
            }

            _isLighthouseSweepActive = true;

            try
            {
                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.UI,
                    "InterCore: Lighthouse Sweep effect activated - another instance detected!");

				// Get all visible frame windows
				var allFrames = Application.Current.Windows.OfType<NonActivatingWindow>()
                    .Where(w => w.Visibility == Visibility.Visible)
                    .OrderBy(w => w.Left)
                    .ToList();

                if (allFrames.Count == 0)
                {
                    LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.UI,
                        "InterCore: No visible frames found for Lighthouse Sweep");
                    _isLighthouseSweepActive = false;
                    return;
                }

                // Store original opacities
                var originalOpacities = new Dictionary<NonActivatingWindow, double>();
                foreach (var frame in allFrames)
                {
                    originalOpacities[frame] = frame.Opacity;

					// If frame has high tint (low opacity), fade it to 0.4
					if (frame.Opacity > 0.4)
                    {
                        var fadeOut = new DoubleAnimation
                        {
                            To = 0.4,
                            Duration = TimeSpan.FromMilliseconds(400),
                            FillBehavior = FillBehavior.HoldEnd
                        };
						frame.BeginAnimation(UIElement.OpacityProperty, fadeOut);
                    }
                }
                PlaySweepSound();

				// Create wave effect across frames
				for (int i = 0; i < allFrames.Count; i++)
                {
                    var frame = allFrames[i];
                    int delay = i * 150;

                    var delayTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(delay) };
                    delayTimer.Tick += (s, e) =>
                    {
                        ApplyLighthouseGlowToFrame(frame);
                        ((DispatcherTimer)s).Stop();
                    };
                    delayTimer.Start();
                }

                // Restore original opacities with fade-in
                var restoreTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
                restoreTimer.Tick += (s, e) =>
                {
                    try
                    {
                        foreach (var frame in allFrames)
                        {
                            if (originalOpacities.ContainsKey(frame))
                            {
                                var fadeIn = new DoubleAnimation
                                {
                                    To = originalOpacities[frame],
                                    Duration = TimeSpan.FromMilliseconds(400),
                                    FillBehavior = FillBehavior.HoldEnd
                                };
								frame.BeginAnimation(UIElement.OpacityProperty, fadeIn);
                            }
                        }

                        _isLighthouseSweepActive = false;
                        restoreTimer.Stop();
                    }
                    catch (Exception ex)
                    {
                        LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI,
                            $"InterCore: Error restoring opacities: {ex.Message}");
                        _isLighthouseSweepActive = false;
                    }
                };
                restoreTimer.Start();
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI,
                    $"InterCore: Error in Lighthouse Sweep activation: {ex.Message}");
                _isLighthouseSweepActive = false;
            }
        }

        private static void ApplyLighthouseGlowToFrame(NonActivatingWindow frame)
        {
            try
            {
                var lighthouseGlow = new DropShadowEffect
                {
                    Color = Color.FromRgb(255, 215, 0), // Gold color
                    BlurRadius = 25,
                    ShadowDepth = 0,
                    Opacity = 0
                };

                var originalEffect = frame.Effect;
				frame.Effect = lighthouseGlow;

                var pulseAnimation = new DoubleAnimation
                {
                    From = 0,
                    To = 0.9,
                    Duration = TimeSpan.FromMilliseconds(200),
                    AutoReverse = true,
                    RepeatBehavior = new RepeatBehavior(3)
                };

                pulseAnimation.Completed += (s, e) =>
                {
                    try
                    {
						frame.Effect = originalEffect;
                    }
                    catch { }
                };

                lighthouseGlow.BeginAnimation(DropShadowEffect.OpacityProperty, pulseAnimation);
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI,
                    $"InterCore: Error applying lighthouse glow to frame '{frame?.Title}': {ex.Message}");
            }
        }

        #endregion

        #region Private Methods - Epic Fireworks (limbo666)

        private static void ActivateSparkleEffect()
        {
            LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.UI, "InterCore: Epic Fireworks effect activated for limbo666!");

            try
            {
                _sparkleOverlay = new Window
                {
                    WindowStyle = WindowStyle.None,
                    AllowsTransparency = true,
                    Background = Brushes.Transparent,
                    Topmost = true,
                    ShowInTaskbar = false,
                    WindowState = WindowState.Maximized,
                    IsHitTestVisible = false
                };

                var canvas = new Canvas();
                _sparkleOverlay.Content = canvas;

                var random = new Random();
                var fireworkCount = 32;

                for (int i = 0; i < fireworkCount; i++)
                {
                    var delay = TimeSpan.FromMilliseconds(random.Next(0, 10000));
                    var launchTimer = new DispatcherTimer { Interval = delay };

                    launchTimer.Tick += (s, e) =>
                    {
                        LaunchFirework(canvas, random);
                        ((DispatcherTimer)s).Stop();
                    };
                    launchTimer.Start();
                }

                _sparkleOverlay.Show();

                var closeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(11) };
                closeTimer.Tick += (s, e) =>
                {
                    _sparkleOverlay?.Close();
                    _sparkleOverlay = null;
                    closeTimer.Stop();
                };
                closeTimer.Start();
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI, $"InterCore: Error in Epic Fireworks activation: {ex.Message}");
                _sparkleOverlay?.Close();
                _sparkleOverlay = null;
            }
        }

        private static void LaunchFirework(Canvas canvas, Random random)
        {
            var launchX = random.Next(100, (int)SystemParameters.PrimaryScreenWidth - 100);
            var launchY = (int)SystemParameters.PrimaryScreenHeight - 50;
            var explodeX = launchX + random.Next(-200, 200);
            var explodeY = random.Next(100, (int)SystemParameters.PrimaryScreenHeight / 2);

            CreateRocketTrail(canvas, launchX, launchY, explodeX, explodeY, random);

            var travelTime = 1.0 + random.NextDouble() * 0.5;
            var explodeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(travelTime) };
            explodeTimer.Tick += (s, e) =>
            {
                CreateFireworkExplosion(canvas, explodeX, explodeY, random);
                ((DispatcherTimer)s).Stop();
            };
            explodeTimer.Start();
        }

        private static void CreateRocketTrail(Canvas canvas, double startX, double startY, double endX, double endY, Random random)
        {
            var rocket = new Ellipse
            {
                Width = 4,
                Height = 8,
                Fill = new SolidColorBrush(Colors.Orange),
                Effect = new DropShadowEffect
                {
                    Color = Colors.Yellow,
                    BlurRadius = 8,
                    ShadowDepth = 0
                }
            };

            Canvas.SetLeft(rocket, startX);
            Canvas.SetTop(rocket, startY);
            canvas.Children.Add(rocket);

            var duration = TimeSpan.FromSeconds(1.0 + random.NextDouble() * 0.5);
            var moveXAnimation = new DoubleAnimation(startX, endX, duration) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
            var moveYAnimation = new DoubleAnimation(startY, endY, duration) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };

            CreateTrail(canvas, startX, startY, endX, endY, duration.TotalSeconds);

            rocket.BeginAnimation(Canvas.LeftProperty, moveXAnimation);
            rocket.BeginAnimation(Canvas.TopProperty, moveYAnimation);

            var removeTimer = new DispatcherTimer { Interval = duration };
            removeTimer.Tick += (s, e) =>
            {
                canvas.Children.Remove(rocket);
                ((DispatcherTimer)s).Stop();
            };
            removeTimer.Start();
        }

        private static void CreateTrail(Canvas canvas, double startX, double startY, double endX, double endY, double duration)
        {
            var random = new Random();
            var trailParticles = 15;

            for (int i = 0; i < trailParticles; i++)
            {
                var delay = (duration / trailParticles) * i;
                var progress = (double)i / trailParticles;
                var particleX = startX + (endX - startX) * progress;
                var particleY = startY + (endY - startY) * progress;

                var trailTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(delay) };
                trailTimer.Tick += (s, e) =>
                {
                    var trail = new Ellipse
                    {
                        Width = 2,
                        Height = 2,
                        Fill = new SolidColorBrush(Color.FromArgb(150, 255, 165, 0)),
                        Effect = new BlurEffect { Radius = 1 }
                    };

                    Canvas.SetLeft(trail, particleX + random.Next(-3, 3));
                    Canvas.SetTop(trail, particleY + random.Next(-3, 3));
                    canvas.Children.Add(trail);

                    var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(0.8));
                    trail.BeginAnimation(UIElement.OpacityProperty, fadeOut);

                    var removeTrail = new DispatcherTimer { Interval = TimeSpan.FromSeconds(0.8) };
                    removeTrail.Tick += (s2, e2) =>
                    {
                        canvas.Children.Remove(trail);
                        ((DispatcherTimer)s2).Stop();
                    };
                    removeTrail.Start();

                    ((DispatcherTimer)s).Stop();
                };
                trailTimer.Start();
            }
        }

        private static void CreateFireworkExplosion(Canvas canvas, double centerX, double centerY, Random random)
        {
            var explosionTypes = new[] { "Burst", "Ring", "Willow" };
            var explosionType = explosionTypes[random.Next(explosionTypes.Length)];

            var colorSchemes = new[]
            {
                new[] { Colors.Red, Colors.Orange, Colors.Yellow },
                new[] { Colors.Blue, Colors.Cyan, Colors.White },
                new[] { Colors.Green, Colors.Lime, Colors.Yellow },
                new[] { Colors.Purple, Colors.Magenta, Colors.Pink },
                new[] { Colors.Gold, Colors.Orange, Colors.White }
            };
            var colors = colorSchemes[random.Next(colorSchemes.Length)];

            switch (explosionType)
            {
                case "Burst": CreateBurstExplosion(canvas, centerX, centerY, colors, random); break;
                case "Ring": CreateRingExplosion(canvas, centerX, centerY, colors, random); break;
                case "Willow": CreateWillowExplosion(canvas, centerX, centerY, colors, random); break;
            }
        }

        private static void CreateBurstExplosion(Canvas canvas, double centerX, double centerY, Color[] colors, Random random)
        {
            var particleCount = 60 + random.Next(40);
            for (int i = 0; i < particleCount; i++)
            {
                var angle = (2 * Math.PI * i) / particleCount + random.NextDouble() * 0.5;
                var velocity = 80 + random.Next(120);
                var size = 3 + random.Next(5);

                var particle = new Ellipse
                {
                    Width = size,
                    Height = size,
                    Fill = new SolidColorBrush(colors[random.Next(colors.Length)]),
                    Effect = new DropShadowEffect { Color = Colors.White, BlurRadius = size * 2, ShadowDepth = 0 }
                };

                Canvas.SetLeft(particle, centerX);
                Canvas.SetTop(particle, centerY);
                canvas.Children.Add(particle);

                var endX = centerX + Math.Cos(angle) * velocity;
                var endY = centerY + Math.Sin(angle) * velocity;

                var moveXAnimation = new DoubleAnimation(centerX, endX, TimeSpan.FromSeconds(2.5)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
                var moveYAnimation = new DoubleAnimation(centerY, endY + 100, TimeSpan.FromSeconds(2.5)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } };
                var fadeAnimation = new DoubleAnimation(1.0, 0, TimeSpan.FromSeconds(2.5)) { BeginTime = TimeSpan.FromSeconds(0.3) };

                particle.BeginAnimation(Canvas.LeftProperty, moveXAnimation);
                particle.BeginAnimation(Canvas.TopProperty, moveYAnimation);
                particle.BeginAnimation(UIElement.OpacityProperty, fadeAnimation);

                var removeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
                removeTimer.Tick += (s, e) => { canvas.Children.Remove(particle); ((DispatcherTimer)s).Stop(); };
                removeTimer.Start();
            }
        }

        private static void CreateRingExplosion(Canvas canvas, double centerX, double centerY, Color[] colors, Random random)
        {
            var particleCount = 36;
            var radius = 120 + random.Next(80);

            for (int i = 0; i < particleCount; i++)
            {
                var angle = (2 * Math.PI * i) / particleCount;
                var size = 4 + random.Next(3);

                var particle = new Ellipse
                {
                    Width = size,
                    Height = size,
                    Fill = new SolidColorBrush(colors[random.Next(colors.Length)]),
                    Effect = new DropShadowEffect { Color = Colors.White, BlurRadius = 10, ShadowDepth = 0 }
                };

                Canvas.SetLeft(particle, centerX);
                Canvas.SetTop(particle, centerY);
                canvas.Children.Add(particle);

                var endX = centerX + Math.Cos(angle) * radius;
                var endY = centerY + Math.Sin(angle) * radius;

                var moveXAnimation = new DoubleAnimation(centerX, endX, TimeSpan.FromSeconds(1.5));
                var moveYAnimation = new DoubleAnimation(centerY, endY + 50, TimeSpan.FromSeconds(2.0));
                var fadeAnimation = new DoubleAnimation(1.0, 0, TimeSpan.FromSeconds(2.0)) { BeginTime = TimeSpan.FromSeconds(0.5) };

                particle.BeginAnimation(Canvas.LeftProperty, moveXAnimation);
                particle.BeginAnimation(Canvas.TopProperty, moveYAnimation);
                particle.BeginAnimation(UIElement.OpacityProperty, fadeAnimation);

                var removeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
                removeTimer.Tick += (s, e) => { canvas.Children.Remove(particle); ((DispatcherTimer)s).Stop(); };
                removeTimer.Start();
            }
        }

        private static void CreateWillowExplosion(Canvas canvas, double centerX, double centerY, Color[] colors, Random random)
        {
            var streamCount = 12 + random.Next(8);
            for (int stream = 0; stream < streamCount; stream++)
            {
                var angle = (2 * Math.PI * stream) / streamCount;
                var particlesPerStream = 15 + random.Next(10);

                for (int i = 0; i < particlesPerStream; i++)
                {
                    var delay = i * 0.05;
                    var distance = (i + 1) * (20 + random.Next(15));

                    var delayTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(delay) };
                    delayTimer.Tick += (s, e) =>
                    {
                        var particle = new Ellipse
                        {
                            Width = 3,
                            Height = 3,
                            Fill = new SolidColorBrush(colors[random.Next(colors.Length)]),
                            Effect = new DropShadowEffect { Color = Colors.Gold, BlurRadius = 6, ShadowDepth = 0 }
                        };

                        Canvas.SetLeft(particle, centerX);
                        Canvas.SetTop(particle, centerY);
                        canvas.Children.Add(particle);

                        var endX = centerX + Math.Cos(angle) * distance;
                        var endY = centerY + Math.Sin(angle) * distance + distance * 0.8;

                        var moveXAnimation = new DoubleAnimation(centerX, endX, TimeSpan.FromSeconds(3.0));
                        var moveYAnimation = new DoubleAnimation(centerY, endY, TimeSpan.FromSeconds(3.0)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } };
                        var fadeAnimation = new DoubleAnimation(1.0, 0, TimeSpan.FromSeconds(2.5)) { BeginTime = TimeSpan.FromSeconds(0.5) };

                        particle.BeginAnimation(Canvas.LeftProperty, moveXAnimation);
                        particle.BeginAnimation(Canvas.TopProperty, moveYAnimation);
                        particle.BeginAnimation(UIElement.OpacityProperty, fadeAnimation);

                        var removeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3.5) };
                        removeTimer.Tick += (s2, e2) => { canvas.Children.Remove(particle); ((DispatcherTimer)s2).Stop(); };
                        removeTimer.Start();

                        ((DispatcherTimer)s).Stop();
                    };
                    delayTimer.Start();
                }
            }
        }

        #endregion

        private static void PlaySweepSound()
        {
            if (SettingsManager.EnableSounds == false) return;
            try
            {
                using (Stream soundStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Desktop_Frames.Resources.sweep-sound-effect-240243.wav"))
                {
                    if (soundStream != null)
                    {
                        using (SoundPlayer player = new SoundPlayer(soundStream)) { player.Play(); }
                    }
                }
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI, $"Error playing sound: {ex.Message}");
            }
        }

        #region Seasonal Easter Eggs (Winter Theme)

        private static void ActivateWinterTheme()
        {
            if (_isWinterActive) return;
            _isWinterActive = true;

            LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.UI, "InterCore: Winter Theme (Xmas Easter Egg) activated!");

            // Run physics loop at ~30 FPS (33ms) for smooth animation without heavy CPU load
            _winterTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
            _winterTimer.Tick += WinterPhysicsLoop;
            _winterTimer.Start();
        }

        /// <summary>
        /// Activates New Year's mode. This is not a separate physics loop —
        /// it shares the winter loop entirely, and simply sets a flag that
        /// the injection and tick code branch on to add the New Year's
        /// layer. Sharing the loop is what keeps the two seasonal effects
        /// from ever running simultaneously and doubling CPU cost, and it
        /// also guarantees that a user crossing from one to the other sees
        /// no discontinuity: the moon, mist, and ground are already there.
        ///
        /// The flag must be set before ActivateWinterTheme is called,
        /// because frames injected during the first winter tick will read
        /// it to decide whether to add the horizon glow, watermark, and
        /// warmer star colour.
        /// </summary>
        private static void ActivateNewYearTheme()
        {
            _isNewYearActive = true;
            LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.UI, "InterCore: New Year Theme activated!");
            ActivateWinterTheme();
        }

        private static void WinterPhysicsLoop(object sender, EventArgs e)
        {
            _winterTickCounter++;
            var time = DateTime.Now.TimeOfDay.TotalSeconds;

            // 1. Every ~2 seconds (60 ticks), scan for newly created frames that need a snow canvas
            if (_winterTickCounter >= 60)
            {
                _winterTickCounter = 0;
                var currentFrames = Application.Current.Windows.OfType<NonActivatingWindow>().ToList();
                foreach (var frame in currentFrames)
                {
                    if (frame.IsLoaded && !_winterContexts.ContainsKey(frame))
                    {
                        InjectSnowCanvas(frame);
                    }
                }
            }

            // 2. Update Sky, Stars, and Ground Accumulation Geometry
            foreach (var kvp in _winterContexts)
            {
                var window = kvp.Key;
                var ctx = kvp.Value;
                if (window == null || !window.IsLoaded) continue;

                double w = Math.Max(10, window.ActualWidth);
                double h = Math.Max(10, window.ActualHeight);
                double segWidth = w / (ctx.Segments - 1);

                // --- Sky gradient tracks the frame size ---
                ctx.SkyGradient.Width = w;
                ctx.SkyGradient.Height = Math.Max(60, h * 0.40);

                // --- Year watermark stays proportioned to the frame ---
                //
                // The mark's FontSize is computed from the frame's width
                // (0.45) with a floor of 48px so even a tiny frame gets a
                // legible number. This runs every tick, so a frame that
                // the user drags from 200px to 800px wide keeps a huge
                // year throughout the drag rather than one that grows
                // only when the frame is first injected.
                //
                // Guarded on null so the winter (non-New-Year's) theme
                // never touches this code — YearWatermark is only created
                // when _isNewYearActive is true.
                if (ctx.YearWatermark != null)
                {
                    // Must match the initial sizing in InjectSnowCanvas
                    // (0.225 of frame width, floor 24). If these two ever
                    // diverge, the year jumps size on the first resize
                    // tick — a subtle but visible glitch.
                    ctx.YearWatermark.FontSize = Math.Max(24, w * 0.225);
                }

                // --- Horizon glow tracks the frame size and stays at the bottom ---
                //
                // This block only runs for the New Year's theme. HorizonGlow
                // is null for plain winter frames (see InjectSnowCanvas, where
                // the rectangle is only created when _isNewYearActive is true).
                // The null check here is what keeps the winter theme free of
                // New Year's state.
                //
                // The glow is anchored to the frame's bottom edge by computing
                // Top from the live height. This is what makes the glow follow
                // the frame when the user resizes it — a Top-anchored glow
                // would float in the middle of a taller frame and drop off
                // the bottom of a shorter one.
                if (ctx.HorizonGlow != null)
                {
                    ctx.HorizonGlow.Width = w;
                    ctx.HorizonGlow.Height = Math.Max(40, h * 0.22);
                    Canvas.SetLeft(ctx.HorizonGlow, 0);
                    Canvas.SetTop(ctx.HorizonGlow, h - ctx.HorizonGlow.Height);
                }

                // --- Star twinkle ---

                // --- Moon positioning ---
                //
                // The moon Canvas is 160x160 with the disc centred at
                // (80, 80). Anchoring it at (w - 180, -10) puts the disc
                // centre at (w - 100, 70) in frame coordinates: upper-right,
                // roughly 100px in from the right edge and 70px down from
                // the top.
                //
                // That placement is chosen so the moon:
                //   - sits below the title bar, so it never fights the
                //     title text or the lock icon for attention,
                //   - is far enough from the right edge that a narrow
                //     frame does not clip the haze into an obvious
                //     half-circle,
                //   - and stays visually consistent across frames of very
                //     different widths (the disc is always the same
                //     distance from the right edge, not a fraction of the
                //     frame width), which is what makes the moons in a
                //     stacked set look like the same moon seen through
                //     different windows rather than like a resizing
                //     stamp.
                Canvas.SetLeft(ctx.MoonCanvas, w - 180);
                Canvas.SetTop(ctx.MoonCanvas, -10);

                // --- Slow haze pulse ---
                //
                // The atmospheric haze around the moon breathes on a ~25-
                // second cycle. This is deliberately far slower than
                // anything else in the scene: mist at a distance moves on
                // a scale the eye reads as "still", so the pulse is
                // registered subconsciously rather than watched. The
                // 0.75–1.00 opacity range keeps the moon solid; the haze
                // only ever brightens or dims, never threatens to vanish.
                if (ctx.MoonHaze != null)
                {
                    double hazePulse = 0.875 + Math.Sin(time * 0.25) * 0.125;
                    ctx.MoonHaze.Opacity = hazePulse;
                }

                // --- Slow mist drift across the moon ---
                //
                // A very slow horizontal sweep, ±8px on a ~40-second cycle.
                // Different frequency from the haze pulse so the two never
                // sync into a visible metronome. This is the "mist crossing
                // the moon" read: a moon behind a shifting veil of cloud,
                // not a stamped disc.
                if (ctx.MoonCloudDrift != null)
                {
                    ctx.MoonCloudDrift.X = Math.Sin(time * 0.16) * 8.0;
                }



                // Twinkle. Two changes from the previous formula:
                //
                //   1. Deeper swing. The multiplier range is now
                //      [0.30, 1.00] instead of [0.60, 1.00]. A star
                //      spends part of its cycle genuinely faint — not
                //      blinking fully out, but dipping to the edge of
                //      visibility — which is what "twinkle" actually
                //      looks like on a cold clear night. A shallow
                //      twinkle reads as noise; a deep one reads as a
                //      star.
                //
                //   2. Per-star speed. Each star uses its own frequency
                //      (assigned at spawn), so the field pulses
                //      independently rather than as a single breathing
                //      mass. With twelve stars at twelve different
                //      speeds and twelve different phases, no two ever
                //      line up, and the eye reads the field as "stars
                //      doing their own thing", not as an animation.
                //
                // Multiplying base × twinkle: a star with base 0.65
                // and a deep twinkle dips to 0.65 × 0.30 ≈ 0.20 for a
                // fraction of a second, which is right at the
                // threshold where a 2px dot fades from view. That is
                // the moment the eye reads as "the star winked".
                // Defensive guard: only iterate when every parallel array
                // has the same length as the star list. A missing or
                // mismatched array would otherwise throw on the dispatcher
                // timer's tick, which is especially hard to diagnose
                // because the stack trace points at the timer callback, not
                // at the code that built the context. Skipping the twinkle
                // for a frame is harmless — the stars just stay at their
                // last opacity until the arrays are fixed.
                if (ctx.StarSpeeds != null &&
                    ctx.StarPhases != null &&
                    ctx.StarBaseOpacity != null &&
                    ctx.StarSpeeds.Length == ctx.Stars.Count &&
                    ctx.StarPhases.Length == ctx.Stars.Count &&
                    ctx.StarBaseOpacity.Length == ctx.Stars.Count)
                {
                    // Collective pre-burst brightening. In the ~1.5 seconds
                    // before a firework launches, every star in the frame
                    // brightens by up to 15% and then fades back. The eye
                    // reads this as the sky itself anticipating the burst,
                    // which is the difference between "a firework happened"
                    // and "the moment was set up". The ramp is asymmetric —
                    // slow rise, quick fall — because the fall coincides
                    // with the burst's own brightness, so a slow fall would
                    // be visually drowned.
                    double starBoost = 1.0;
                    if (_isNewYearActive && ctx.NextFireworkTime != double.MaxValue)
                    {
                        double untilBurst = ctx.NextFireworkTime - time;
                        if (untilBurst > 0 && untilBurst < 1.5)
                        {
                            // Ramp from 0 at 1.5s before the burst, to 1 at
                            // the moment of the burst.
                            double progress = 1.0 - (untilBurst / 1.5);
                            starBoost = 1.0 + progress * 0.15;
                        }
                    }

                    for (int i = 0; i < ctx.Stars.Count; i++)
                    {
                        double twinkle = 0.65 + Math.Sin(time * ctx.StarSpeeds[i] + ctx.StarPhases[i]) * 0.35;
                        ctx.Stars[i].Opacity = Math.Min(1.0, ctx.StarBaseOpacity[i] * twinkle * starBoost);

                        // The glint appears only at the peak of the star's
                        // cycle, so a star that carries a glint "flashes"
                        // once per twinkle — which is exactly what a real
                        // bright star looks like at the threshold of
                        // atmospheric scintillation.
                        //
                        // The shine factor is zero for twinkle <= 0.72 and
                        // ramps to 1.0 at twinkle == 1.0. The 1.6 exponent
                        // sharpens the ramp: instead of the glint smoothly
                        // fading in from nothing, it "arrives" quickly near
                        // the top of the cycle. That quickness is what the
                        // eye reads as a flash rather than as a fade.
                        //
                        // Guarded against a null entry: only stars above
                        // the brightness threshold were given a glint, so
                        // most entries in ctx.StarGlints are null.
                        if (ctx.StarGlints != null &&
                            i < ctx.StarGlints.Count &&
                            ctx.StarGlints[i] != null)
                        {
                            double shineFactor = Math.Max(0, (twinkle - 0.72) / 0.28);
                            shineFactor = Math.Pow(shineFactor, 1.6);
                            ctx.StarGlints[i].Opacity = shineFactor * ctx.StarBaseOpacity[i];
                        }
                    }
                }
                // --- Ground geometry ---
                var groundPoints = new PointCollection();
                groundPoints.Add(new Point(w, h)); // Bottom right
                groundPoints.Add(new Point(0, h)); // Bottom left

                for (int i = 0; i < ctx.Segments; i++)
                {
                    groundPoints.Add(new Point(i * segWidth, h - ctx.Accumulation[i]));
                }
                ctx.Ground.Points = groundPoints;

                // --- Crest line and its shadow ---
                //
                // Both trace the ground's top edge. The crest sits 1px
                // above (bright band on the lit edge), the shadow 2-6px
                // below (cool blue in the recessed side). They share the
                // accumulation array so their contours stay locked to the
                // ground even as it grows.
                var crestPoints = new PointCollection();
                var shadowPoints = new PointCollection();

                // Top edge of the crest, left to right.
                for (int i = 0; i < ctx.Segments; i++)
                {
                    double x = i * segWidth;
                    double topY = h - ctx.Accumulation[i];
                    crestPoints.Add(new Point(x, topY - 1.0));
                    shadowPoints.Add(new Point(x, topY + 2.0));
                }
                // Bottom edge of the crest, right to left.
                for (int i = ctx.Segments - 1; i >= 0; i--)
                {
                    double x = i * segWidth;
                    double topY = h - ctx.Accumulation[i];
                    crestPoints.Add(new Point(x, topY + 2.0));
                    shadowPoints.Add(new Point(x, topY + 6.0));
                }
                ctx.CrestLine.Points = crestPoints;
                ctx.CrestShadow.Points = shadowPoints;
            }

            // -----------------------------------------------------------------
            // 2.7. HUGE FLAKE SCHEDULER
            //
            // Each frame independently decides when its next huge flake
            // arrives. The first one is scheduled at injection (10–35s),
            // and each subsequent one is scheduled immediately after the
            // previous fires, with a fresh 15–45s gap.
            //
            // The huge flake is created with IsHuge = true, added to the
            // shared _snowflakes list, and left for the main update loop
            // below. Creating it through the same list keeps a single
            // physics path for every flake type — the flag is the only
            // thing that distinguishes them, and it is checked in exactly
            // two places: here (spawn) and inside RespawnSnowflake
            // (parameters) and the melt handler (removal).
            // -----------------------------------------------------------------
            foreach (var kvp in _winterContexts)
            {
                var window = kvp.Key;
                var ctx = kvp.Value;
                if (window == null || !window.IsLoaded) continue;

                if (time >= ctx.NextHugeFlakeTime)
                {
                    // _winterContexts is keyed on Window (its natural base
                    // type) but Snowflake.ParentWindow is typed as
                    // NonActivatingWindow, because only frames of that type
                    // are ever injected into the winter theme. The cast
                    // below is safe: every key in this dictionary was put
                    // there by InjectSnowCanvas, whose parameter is already
                    // a NonActivatingWindow.
                    var hugeFlake = new Snowflake
                    {
                        ParentWindow = (NonActivatingWindow)window,
                        IsHuge = true
                    };
                    RespawnSnowflake(hugeFlake);
                    _snowflakes.Add(hugeFlake);

                    // Next huge flake: 15–45 seconds from now. Randomised
                    // so a set of frames never falls into a rhythm.
                    ctx.NextHugeFlakeTime = time + 15 + _random.NextDouble() * 30;
                }
            }

            // -----------------------------------------------------------------
            // NEW YEAR'S PHYSICS
            //
            // Guarded on _isNewYearActive so the loops never run for the
            // plain winter theme, and so a user who has the app open
            // across the Dec 31 → Jan 1 boundary without restarting still
            // sees a coherent scene (nothing switches under them — the
            // change requires a restart, exactly as every other seasonal
            // theme does).
            // -----------------------------------------------------------------
            if (_isNewYearActive)
            {
                // --- Firework launch scheduler ---
                //
                // Each frame independently decides when its next firework
                // launches. The first launch is scheduled at injection
                // (8–28 seconds after frame creation), and each subsequent
                // launch is scheduled 25–60 seconds after the previous one
                // fires. Staggering across frames is what produces the
                // impression of "bursts happening somewhere on the desktop"
                // rather than five frames launching in unison.
                foreach (var kvp in _winterContexts)
                {
                    var window = kvp.Key;
                    var ctx = kvp.Value;
                    if (window == null || !window.IsLoaded) continue;

                    if (time >= ctx.NextFireworkTime)
                    {
                        SpawnFirework((NonActivatingWindow)window);
                        ctx.NextFireworkTime = time + 25 + _random.NextDouble() * 35;
                    }
                }

                // --- Comet tick ---
                //
                // Comets rise, gently sway side to side, and burst when
                // they reach their apex. The sway is deliberate: a comet
                // that rose perfectly vertically would read as a laser,
                // whereas a comet that wobbles slightly reads as something
                // launched, aerodynamic, imperfect — which is what real
                // fireworks look like in the air.
                for (int i = _fireworkComets.Count - 1; i >= 0; i--)
                {
                    var comet = _fireworkComets[i];
                    if (comet.ParentWindow == null || !comet.ParentWindow.IsLoaded)
                    {
                        _fireworkComets.RemoveAt(i);
                        continue;
                    }

                    comet.Y -= comet.Speed;
                    double currentX = comet.BaseX
                                    + Math.Sin(time * 1.8 + comet.SwayPhase) * comet.SwayAmplitude;

                    Canvas.SetLeft(comet.Visual, currentX - comet.Visual.Width / 2.0);
                    Canvas.SetTop(comet.Visual, comet.Y - comet.Visual.Height / 2.0);

                    if (comet.Y <= comet.ApexY)
                    {
                        BurstFirework(comet);
                        var canvas = FindWinterCanvas(comet.ParentWindow);
                        if (canvas != null) canvas.Children.Remove(comet.Visual);
                        _fireworkComets.RemoveAt(i);
                    }
                }

                // --- Spark tick ---
                //
                // Sparks obey gravity: VY increases each tick, which makes
                // the burst's outer edges curve downward into the classic
                // "willow" shape. Opacity fades linearly with remaining
                // life, so the last sparks of a burst disappear gracefully
                // rather than winking out.
                for (int i = _fireworkSparks.Count - 1; i >= 0; i--)
                {
                    var spark = _fireworkSparks[i];
                    if (spark.ParentWindow == null || !spark.ParentWindow.IsLoaded)
                    {
                        _fireworkSparks.RemoveAt(i);
                        continue;
                    }

                    spark.Life -= 1.0 / 30.0; // one tick at 30 FPS
                    if (spark.Life <= 0)
                    {
                        var canvas = FindWinterCanvas(spark.ParentWindow);
                        if (canvas != null) canvas.Children.Remove(spark.Visual);
                        _fireworkSparks.RemoveAt(i);
                        continue;
                    }

                    // Position update first, then gravity — this ordering
                    // gives a slightly arcing trajectory rather than a
                    // strictly parabolic one, which reads as more natural.
                    spark.X += spark.VX;
                    spark.Y += spark.VY;
                    spark.VY += 0.35; // gravity constant, per tick at 30 FPS

                    Canvas.SetLeft(spark.Visual, spark.X);
                    Canvas.SetTop(spark.Visual, spark.Y);
                    spark.Visual.Opacity = spark.Life / spark.MaxLife;
                }

                // --- Glitter on the year ---
                //
                // Glitter sparkles are confined to the year watermark's
                // bounding box. Two things drive the effect:
                //
                //   1. The bounds are read live from the TextBlock's
                //      ActualWidth/ActualHeight rather than estimated from
                //      the font size, so a frame that has been resized
                //      keeps its glitter exactly over the digits. If the
                //      first tick fires before layout has measured the
                //      text, ActualWidth is zero and no sparkles are
                //      spawned that tick — a harmless one-frame skip.
                //
                //   2. The spawn rate is fast (2–4 sparkles every
                //      200–400ms), which produces roughly 4–8 active
                //      sparkles at any moment. That density reads as
                //      "the year is shimmering" rather than as "one
                //      sparkle happened". Sparkle lifetimes are short
                //      (250–450ms) precisely so the eye never fixes on
                //      any one of them.
                foreach (var kvp in _winterContexts)
                {
                    // Cast because _winterContexts is keyed on Window while
                    // the particle classes are typed against
                    // NonActivatingWindow. Every key in this dictionary was
                    // inserted by InjectSnowCanvas, whose parameter is
                    // already a NonActivatingWindow, so the cast is safe.
                    var frameWindow = kvp.Key as NonActivatingWindow;
                    if (frameWindow == null || !frameWindow.IsLoaded) continue;
                    var ctx = kvp.Value;
                    if (ctx.GlitterCanvas == null || ctx.YearWatermark == null) continue;

                    // w and h mirror the values computed in the geometry loop
                    // above, but cannot be inherited from there: this
                    // foreach is nested inside the outer
                    // if (_isNewYearActive) block, which is a sibling of
                    // that geometry loop, not an enclosing scope. Computing
                    // them here from the live frame size is the cleanest
                    // way to reference them without moving the whole
                    // glitter scheduler up into the geometry loop.
                    double w = Math.Max(10, frameWindow.ActualWidth);
                    double h = Math.Max(10, frameWindow.ActualHeight);

                    if (time >= ctx.NextGlitterTime)
                    {
                        double yearW = ctx.YearWatermark.ActualWidth;
                        double yearH = ctx.YearWatermark.ActualHeight;

                        if (yearW > 8 && yearH > 8)
                        {
                            // The year is centered in the rootGrid with
                            // HorizontalAlignment.Center / VerticalAlignment
                            // .Center, so its centre coincides with the
                            // frame's centre. The bounds are therefore
                            // (w/2 ± yearW/2, h/2 ± yearH/2) in canvas
                            // coordinates, which is exactly what the
                            // GlitterCanvas uses.
                            double yearCx = w / 2.0;
                            double yearCy = h / 2.0;
                            double left = yearCx - yearW / 2.0;
                            double top = yearCy - yearH / 2.0;

                            // Two to four sparkles per batch. Enough to
                            // read as "shimmering", few enough that any
                            // individual sparkle still registers.
                            int batchSize = 2 + _random.Next(3);

                            for (int s = 0; s < batchSize; s++)
                            {
                                // Sparkle size 1.5–3.0px. Larger reads as
                                // a particle; smaller reads as noise.
                                double spSize = 1.5 + _random.NextDouble() * 1.5;

                                // Colour: mostly warm gold, occasionally
                                // bright white. The white sparkles are the
                                // "highlights" that give the effect its
                                // sense of light catching at an angle.
                                var glColor = _random.NextDouble() < 0.75
                                    ? Color.FromRgb(255, 240, 180)
                                    : Color.FromRgb(255, 255, 240);

                                var spVisual = new Ellipse
                                {
                                    Width = spSize,
                                    Height = spSize,
                                    Fill = new SolidColorBrush(glColor),
                                    IsHitTestVisible = false,
                                    Opacity = 0,
                                    Effect = new DropShadowEffect
                                    {
                                        Color = glColor,
                                        BlurRadius = 4,
                                        ShadowDepth = 0,
                                        Opacity = 0.8
                                    }
                                };

                                // Spawn inside the year's bounds with a
                                // small inset so a sparkle never hangs
                                // off the edge of the outermost digit.
                                double inset = 0.05;
                                double spX = left + (inset + _random.NextDouble() * (1 - 2 * inset)) * yearW;
                                double spY = top + (inset + _random.NextDouble() * (1 - 2 * inset)) * yearH;

                                Canvas.SetLeft(spVisual, spX - spSize / 2.0);
                                Canvas.SetTop(spVisual, spY - spSize / 2.0);
                                ctx.GlitterCanvas.Children.Add(spVisual);

                                // Sparkle lifetime 250–450ms. Short, so
                                // the field never feels stagnant; long
                                // enough that the eye sees the flash
                                // rather than just a blink.
                                double life = 0.25 + _random.NextDouble() * 0.20;

                                _glitter.Add(new Glitter
                                {
                                    Visual = spVisual,
                                    ParentWindow = frameWindow,
                                    X = spX,
                                    Y = spY,
                                    Life = life,
                                    MaxLife = life,
                                    PeakOpacity = 0.85 + _random.NextDouble() * 0.15
                                });
                            }
                        }

                        // Next batch 200–400ms from now. The timing is
                        // deliberately loose rather than a fixed interval,
                        // so the effect never falls into a rhythm the eye
                        // can predict.
                        ctx.NextGlitterTime = time + 0.20 + _random.NextDouble() * 0.20;
                    }
                }

                // --- Glitter tick ---
                //
                // Each sparkle's opacity follows a half-sine over its
                // lifetime: zero at spawn, peak at the middle, zero at
                // death. The curve reads as "the light caught briefly
                // and released" rather than as "the dot faded in and
                // out", which is what makes glitter feel alive.
                for (int i = _glitter.Count - 1; i >= 0; i--)
                {
                    var g = _glitter[i];

                    if (g.ParentWindow == null || !g.ParentWindow.IsLoaded)
                    {
                        _glitter.RemoveAt(i);
                        continue;
                    }

                    g.Life -= 1.0 / 30.0;
                    if (g.Life <= 0)
                    {
                        if (_winterContexts.TryGetValue(g.ParentWindow, out var gctx) && gctx.GlitterCanvas != null)
                        {
                            gctx.GlitterCanvas.Children.Remove(g.Visual);
                        }
                        _glitter.RemoveAt(i);
                        continue;
                    }

                    double t = 1.0 - (g.Life / g.MaxLife);
                    g.Visual.Opacity = Math.Sin(t * Math.PI) * g.PeakOpacity;
                }

                // --- Confetti scheduler ---
                //
                // Sparse: one piece per frame every 6–12 seconds, capped at
                // four active per frame. A denser confetti field would
                // compete with the snow and with the icons for attention,
                // and the sparse treatment is what makes the effect feel
                // elegant rather than busy.
                foreach (var kvp in _winterContexts)
                {
                    var window = kvp.Key;
                    var ctx = kvp.Value;
                    if (window == null || !window.IsLoaded) continue;

                    if (time >= ctx.NextConfettiTime)
                    {
                        int activeForThisFrame = 0;
                        foreach (var c in _confetti) if (c.ParentWindow == window) activeForThisFrame++;

                        // Cap raised 4 → 8 and interval shortened 6–12s →
                        // 1.5–3.5s. Together these roughly triple the
                        // confetti field density: more pieces arriving
                        // more often. The cap is still the real limit —
                        // a frame will stabilise around 8 active pieces
                        // rather than growing without bound, which is
                        // what keeps the field festive instead of
                        // snowed-under.
                        //
                        // If it reads as too busy, raise the interval
                        // before lowering the cap: sparser arrivals with
                        // a higher cap gives "a burst of confetti now
                        // and then", whereas a lower cap with the same
                        // interval gives "a thin constant trickle". The
                        // first reads as celebration, the second as noise.
                        if (activeForThisFrame < 36)
                        {
                            SpawnConfetti((NonActivatingWindow)window);
                        }
                        ctx.NextConfettiTime = time + 1.5 + _random.NextDouble() * 2.0;
                    }
                }

                // --- Confetti tick ---
                //
                // Fall, sway, and tumble. Rotation is what distinguishes
                // confetti from snow at a glance: at the same size and
                // fall speed, a rectangle that spins reads as confetti,
                // while one that does not reads as a bug in the snow
                // code. The rotation speed range is deliberately wide
                // (0.8–2.4 rad/s) so pieces tumble at visibly different
                // rates — otherwise the whole field would pirouette in
                // lockstep.
                for (int i = _confetti.Count - 1; i >= 0; i--)
                {
                    var c = _confetti[i];
                    if (c.ParentWindow == null || !c.ParentWindow.IsLoaded)
                    {
                        _confetti.RemoveAt(i);
                        continue;
                    }

                    c.Life -= 1.0 / 30.0;
                    if (c.Life <= 0 || c.Y > c.ParentWindow.ActualHeight + 20)
                    {
                        var canvas = FindWinterCanvas(c.ParentWindow);
                        if (canvas != null) canvas.Children.Remove(c.Visual);
                        _confetti.RemoveAt(i);
                        continue;
                    }

                    c.Y += c.Speed;
                    double currentX = c.BaseX + Math.Sin(time * c.DriftSpeed) * c.DriftOffset;
                    Canvas.SetLeft(c.Visual, currentX);
                    Canvas.SetTop(c.Visual, c.Y);

                    if (c.Visual.RenderTransform is RotateTransform rot)
                    {
                        rot.Angle = (c.RotationPhase + time * c.RotationSpeed * 57.2958) % 360;
                    }

                    // Fade out over the last 20% of life so a piece does
                    // not disappear in mid-air when its timer expires.
                    double lifeFraction = c.Life / c.MaxLife;
                    c.Visual.Opacity = lifeFraction < 0.2 ? lifeFraction / 0.2 * 0.85 : 0.85;
                }
            }

            // 3. Update Snowflakes
            for (int i = _snowflakes.Count - 1; i >= 0; i--)
            {
                var flake = _snowflakes[i];

                // Clean up if window was closed
                if (flake.ParentWindow == null || !flake.ParentWindow.IsLoaded)
                {
                    _snowflakes.RemoveAt(i);
                    continue;
                }

                if (flake.IsMelting)
                {
                    flake.Visual.Opacity -= flake.MeltRate;
                    if (flake.Visual.Opacity <= 0)
                    {
                        // Huge flakes are one-shot. Instead of respawning,
                        // they are removed from the canvas and from the
                        // active list entirely; a new one will be spawned
                        // when the scheduler next fires. Removing the
                        // visual from the canvas is essential — otherwise
                        // the flake's transparent but still-present Ellipse
                        // would accumulate on the canvas over hours of
                        // runtime, each one a lingering layout participant.
                        if (flake.IsHuge)
                        {
                            var ctx = _winterContexts.TryGetValue(flake.ParentWindow, out var c) ? c : null;
                            if (ctx != null && flake.Visual != null)
                            {
                                ctx.Canvas.Children.Remove(flake.Visual);
                            }
                            _snowflakes.RemoveAt(i);
                            continue;
                        }

                        RespawnSnowflake(flake);
                    }
                }
                else
                {
                    flake.Y += flake.Speed;

                    // Add a natural horizontal sway using a Sine wave based on time
                    double currentX = flake.BaseX + Math.Sin(time * flake.DriftSpeed) * flake.DriftOffset;

                    Canvas.SetLeft(flake.Visual, currentX);
                    Canvas.SetTop(flake.Visual, flake.Y);

                    // Slow tumble as the flake falls. Only the polygon shapes
                    // (hexagon and star) show this visibly — a circle rotated
                    // looks identical to itself — but applying it universally
                    // keeps the code path uniform.
                    if (flake.Visual.RenderTransform is RotateTransform rot)
                    {
                        rot.Angle = (flake.RotationPhase + time * flake.RotationSpeed * 57.2958) % 360;
                    }

                    // Melt when reaching the bottom edge padding
                    if (flake.Y >= flake.ParentWindow.ActualHeight - 15)
                    {
                        if (!flake.IsMelting) // Trigger accumulation once
                        {
                            flake.IsMelting = true;

                            // Huge flakes deliberately do NOT contribute to
                            // the snow accumulation. A 24px flake would
                            // contribute roughly the same amount as a full
                            // wave of regular flakes, producing a sudden
                            // local spike in the drift that reads as a bug.
                            // They fade out where they are, which is also
                            // what real large flakes do when they land on
                            // snow: they are already the same material, so
                            // they simply disappear into the surface.
                            if (flake.IsHuge) continue;

                            // Add accumulation to the ground
                            if (_winterContexts.TryGetValue(flake.ParentWindow, out var ctx))
                            {
                                double w = Math.Max(1, flake.ParentWindow.ActualWidth);
                                double ratio = currentX / w;
                                if (ratio < 0) ratio = 0;
                                if (ratio > 1) ratio = 1;

                                int index = (int)(Math.Round(ratio * (ctx.Segments - 1)));
                                if (index >= 0 && index < ctx.Segments)
                                {
                                    // Increment height gently, capped at its specific hilly max height
                                    ctx.Accumulation[index] = Math.Min(ctx.MaxAccumulation[index], ctx.Accumulation[index] + 1.2);

                                    // Smooth neighboring segments to make it look like a natural contiguous drift
                                    if (index > 0) ctx.Accumulation[index - 1] = Math.Min(ctx.MaxAccumulation[index - 1], ctx.Accumulation[index - 1] + 0.6);
                                    if (index < ctx.Segments - 1) ctx.Accumulation[index + 1] = Math.Min(ctx.MaxAccumulation[index + 1], ctx.Accumulation[index + 1] + 0.6);
                                }
                            }
                        }
                    }
                }
            }
        }

        private static void InjectSnowCanvas(NonActivatingWindow frame)
        {
            try
            {
                // CRITICAL FIX: We must NOT replace frame.Content, because the customization
                // engine expects frame.Content to be the main Border to change its color.
                var mainBorder = frame.Content as Border;
                if (mainBorder == null) return;

                // Don't inject if we already wrapped this window's internal content
                if (mainBorder.Child is Grid originalGrid && originalGrid.Name == "WinterGrid") return;

                // Safely disconnect the internal content from the border
                var originalInner = mainBorder.Child;
                mainBorder.Child = null;

                // Create wrapper
                var rootGrid = new Grid { Name = "WinterGrid" };

                // -----------------------------------------------------------------
                // SKY LAYER — behind the frame's own content.
                //
                // The gradient tints the frame background toward a deep
                // blue-indigo, mirroring the effect on Halloween but with a
                // colder palette. Because it sits behind originalInner, the
                // icons, title text and filter bar stay fully legible — the
                // gradient only affects the frame's background tint.
                //
                // Stars are added on the same canvas, in front of the
                // gradient rectangle so they read crisply through it, but
                // still behind the frame content so they read as distant.
                // -----------------------------------------------------------------
                var skyCanvas = new Canvas { IsHitTestVisible = false, ClipToBounds = true };

                var skyGradient = new Rectangle
                {
                    IsHitTestVisible = false,
                    Width = Math.Max(100, frame.ActualWidth),
                    Height = Math.Max(60, frame.ActualHeight * 0.40),
                    Fill = new LinearGradientBrush
                    {
                        StartPoint = new Point(0, 0),
                        EndPoint = new Point(0, 1),
                        GradientStops = new GradientStopCollection
                        {
                            // Alpha lowered 170 → 130 in the top band so the
                            // frame's own background colour shows through
                            // a little more. Stars in front of this are
                            // not affected by the gradient's opacity
                            // (they paint on top of it), but the small
                            // drop in backdrop darkness makes a faint
                            // 2px dot register as a star rather than as
                            // a dead pixel — the eye picks up the
                            // *contrast* of a dot against its surround,
                            // not its absolute brightness.
                            new GradientStop(Color.FromArgb(130, 18, 28, 62), 0.0),
                            new GradientStop(Color.FromArgb(80, 22, 34, 72), 0.55),
                            new GradientStop(Color.FromArgb(0, 22, 34, 72), 1.0)
                        }
                    }
                };
                skyCanvas.Children.Add(skyGradient);

                // -----------------------------------------------------------------
                // THE YELLOW MOON
                //
                // A honey-gold moon in the upper-right, with a warm halo, a
                // wide atmospheric haze, and two cool blue-grey mist wisps
                // crossing it. The warm/cool contrast between the moon and
                // the mist is what sells the read: a warm light source seen
                // through cold air is the single most recognisable winter
                // night image there is.
                //
                // The moon lives on the sky canvas, which sits behind the
                // frame's own content. That placement is deliberate: the
                // frame's semi-transparent background tints the moon toward
                // the frame's hue, exactly as a real moon seen through a
                // tinted window would be. A moon drawn on top of the
                // frame's content instead reads as a sticker on the front
                // of the scene, which is not the effect we want.
                //
                // The canvas is 160x160 with the disc centred at (80, 80).
                // It has to be larger than the disc because the haze and
                // halo extend roughly four times the disc's radius.
                // Positioning is done in the physics loop; initial placement
                // here is a best-guess so nothing flashes at the origin on
                // the first tick.
                // -----------------------------------------------------------------
                var moonCanvas = new Canvas
                {
                    Width = 160,
                    Height = 160,
                    IsHitTestVisible = false
                };

                // The wide atmospheric haze: a very soft, very faint warm-
                // gold radial wash, roughly 4× the disc's diameter. This is
                // the difference between a crisp vector moon and a moon
                // seen through kilometres of atmosphere. Its opacity is
                // pulsed by the physics loop on a slow cycle (~25s), which
                // makes the moon breathe without ever drawing attention.
                var moonHaze = new Ellipse
                {
                    Width = 160,
                    Height = 160,
                    Fill = new RadialGradientBrush
                    {
                        GradientStops = new GradientStopCollection
                        {
                            new GradientStop(Color.FromArgb(65, 255, 230, 175), 0.0),
                            new GradientStop(Color.FromArgb(38, 255, 230, 175), 0.45),
                            new GradientStop(Color.FromArgb(14, 255, 230, 175), 0.75),
                            new GradientStop(Color.FromArgb(0, 255, 230, 175), 1.0)
                        }
                    }
                };
                moonCanvas.Children.Add(moonHaze);

                // The disc itself. Warm cream at the centre, deepening to
                // honey gold toward the rim, with a soft fade instead of a
                // hard edge. The gradient origin is offset up-left of
                // centre so the moon reads as lit from one side rather than
                // as a flat coin. The four-stop gradient produces the soft
                // limb a real moon shows through atmosphere.
                var moonDisc = new Ellipse
                {
                    Width = 36,
                    Height = 36,
                    Fill = new RadialGradientBrush
                    {
                        GradientOrigin = new Point(0.35, 0.35),
                        Center = new Point(0.5, 0.5),
                        RadiusX = 0.72,
                        RadiusY = 0.72,
                        GradientStops = new GradientStopCollection
                        {
                            new GradientStop(Color.FromArgb(245, 255, 252, 230), 0.0),  // Warm cream core
                            new GradientStop(Color.FromArgb(225, 255, 238, 175), 0.50), // Honey mid
                            new GradientStop(Color.FromArgb(140, 250, 210, 110), 0.82), // Deep gold limb
                            new GradientStop(Color.FromArgb(0, 250, 210, 110), 1.0)     // Fade out
                        }
                    }
                };
                Canvas.SetLeft(moonDisc, 62);
                Canvas.SetTop(moonDisc, 62);
                moonCanvas.Children.Add(moonDisc);

                // Two mist wisps crossing the disc. Cool blue-grey so they
                // contrast against the warm gold moon — a warm source seen
                // through cool air. They use rounded rectangles with a
                // heavy blur so they read as fog rather than as bands, and
                // they share a single TranslateTransform so a drift applied
                // to it moves the pair as one body of mist.
                var moonCloudDrift = new TranslateTransform(0, 0);

                var lowerWisp = new Rectangle
                {
                    Width = 130,
                    Height = 18,
                    RadiusX = 9,
                    RadiusY = 9,
                    Fill = new SolidColorBrush(Color.FromArgb(175, 38, 55, 100)),
                    Effect = new BlurEffect { Radius = 7 },
                    RenderTransform = moonCloudDrift
                };
                Canvas.SetLeft(lowerWisp, 15);
                Canvas.SetTop(lowerWisp, 88);
                moonCanvas.Children.Add(lowerWisp);

                var upperWisp = new Rectangle
                {
                    Width = 78,
                    Height = 10,
                    RadiusX = 5,
                    RadiusY = 5,
                    Fill = new SolidColorBrush(Color.FromArgb(145, 45, 62, 110)),
                    Effect = new BlurEffect { Radius = 6 }
                };
                Canvas.SetLeft(upperWisp, 40);
                Canvas.SetTop(upperWisp, 58);
                moonCanvas.Children.Add(upperWisp);

                skyCanvas.Children.Add(moonCanvas);

                // -----------------------------------------------------------------
                // NEW YEAR'S HORIZON GLOW
                //
                // A wide amber rectangle at the bottom of the sky canvas,
                // fading upward from gold to transparent. The effect reads
                // as distant city lights reflected off low cloud, which is
                // what the frames need to be: not a wilderness, but a
                // window somewhere near a celebration. Without it, the
                // fireworks and confetti feel pasted onto a scene that has
                // no reason for them.
                //
                // Alpha is deliberately low (35 at the base). A stronger
                // glow would compete with the moon's haze and with the
                // ground crest; the horizon should be felt, not seen.
                //
                // The rectangle's position and size are assigned in the
                // physics loop, matching the sky gradient's treatment.
                // Placing it here in the z-order — after the moon, before
                // the stars — means the moon reads as being *above* the
                // horizon (which it is), and the stars read as being in
                // the same distant band as the horizon glow (which they
                // are). A glow placed behind the moon would read as fog.
                // -----------------------------------------------------------------
                Rectangle horizonGlow = null;
                if (_isNewYearActive)
                {
                    horizonGlow = new Rectangle
                    {
                        IsHitTestVisible = false,
                        Width = Math.Max(100, frame.ActualWidth),
                        Height = Math.Max(40, frame.ActualHeight * 0.22),
                        Fill = new LinearGradientBrush
                        {
                            StartPoint = new Point(0, 1),
                            EndPoint = new Point(0, 0),
                            GradientStops = new GradientStopCollection
                            {
                                new GradientStop(Color.FromArgb(35, 255, 190, 110), 0.0),
                                new GradientStop(Color.FromArgb(18, 255, 190, 110), 0.55),
                                new GradientStop(Color.FromArgb(0, 255, 190, 110), 1.0)
                            }
                        }
                    };
                    skyCanvas.Children.Add(horizonGlow);
                }

                // Stars. Scattered across the top 32% of the frame, sized
                // 1.5–2.5px so they read as pinpricks, not dots. Their
                // opacity is driven from the physics loop (twinkle) so we
                // do not need a Storyboard per star.
                var stars = new List<Ellipse>();

                // Twelve stars per frame. A winter night sky at a glance
                // shows half a dozen bright stars; thirty was too dense
                // and read as scattered texture rather than as a sky.
                // Twelve keeps the field sparse enough that each star
                // registers individually, while still giving the
                // composition enough points to feel populated.
                const int StarCount = 12;

                var starPhases = new double[StarCount];
                var starBaseOpacity = new double[StarCount];
                var starSpeeds = new double[StarCount];
                var starGlints = new List<Shape>();

         
                // Star fill. On New Year's the gold shifts a few points
                // warmer — 255, 230, 170 becomes 255, 235, 175 — so the
                // sky reads as "warmer air tonight" rather than "the same
                // sky, one week later". The change is small on purpose:
                // the two seasonal themes should be distinguishable at a
                // glance but not feel like two different skies.
                var starColor = _isNewYearActive
                    ? Color.FromRgb(255, 235, 175)
                    : Color.FromRgb(255, 230, 170);
                var starBrush = new SolidColorBrush(starColor);
                starBrush.Freeze();

                // Star placement. Two coordinates per star, both in the
                // upper part of the frame:
                //
                //   X: uniform across the full width, minus a small margin
                //      so stars never touch the frame edge.
                //   Y: biased toward the top 25% of the frame, where the
                //      sky gradient is darkest and the background is
                //      least cluttered by icons. A slight upward skew
                //      (Math.Pow with exponent 1.6) means most stars sit
                //      high, with a few stragglers lower down — the
                //      pattern a real sky shows.
                //
                // The positions are computed once here. The physics loop
                // only animates opacity, not position, so a frame resize
                // will not re-scatter the stars — they will simply stay
                // where they are relative to the frame's left edge, which
                // for a window the user is dragging is the correct
                // behaviour.
                double starAreaW = Math.Max(60, frame.ActualWidth);
                double starAreaH = Math.Max(40, frame.ActualHeight * 0.25);

                for (int i = 0; i < StarCount; i++)
                {
                    // 2.0–3.4px. Slightly larger than before (was up to 3.2)
                    // because there are fewer stars — each one has to carry
                    // more visual weight now, and a star that is barely
                    // 2px in a sparse field reads as a stuck pixel.
                    double sSize = 2.0 + _random.NextDouble() * 1.4;
                    var star = new Ellipse
                    {
                        Width = sSize,
                        Height = sSize,
                        Fill = starBrush,
                        IsHitTestVisible = false,
                        Opacity = 0.6
                    };

                    // Vertical distribution: strong bias toward the top
                    // means most stars sit high in the sky band, with a
                    // few trickling lower. With only twelve stars, this
                    // bias is more important than before — without it
                    // the field could cluster at random and lose the
                    // "clear night sky" read.
                    double normY = Math.Pow(_random.NextDouble(), 1.8);
                    double starX = 6 + _random.NextDouble() * (starAreaW - 12);
                    double starY = 4 + normY * (starAreaH - 8);

                    Canvas.SetLeft(star, starX);
                    Canvas.SetTop(star, starY);

                    // Brighter overall, because there are fewer stars to
                    // share the eye's attention. Every star now sits in
                    // the 0.65–1.00 band; the eye reads a sparse sky as
                    // "a few bright stars", not as "many dim ones".
                    double baseOpacity = 0.65 + _random.NextDouble() * 0.35;

                    // The brightest third gets a warm halo. With only
                    // twelve stars, the halo becomes more prominent —
                    // a lone gold dot with a soft glow reads as a star;
                    // a lone gold dot alone reads as a marker.
                    if (baseOpacity > 0.85)
                    {
                        star.Effect = new DropShadowEffect
                        {
                            Color = Color.FromRgb(255, 200, 120),
                            BlurRadius = 5,
                            ShadowDepth = 0,
                            Opacity = 0.8
                        };
                    }

                    stars.Add(star);
                    skyCanvas.Children.Add(star);

                    starPhases[i] = _random.NextDouble() * Math.PI * 2;

                    // Per-star twinkle speed. Range spans roughly 2.5×
                    // between the slowest and fastest star (0.4 to 1.1
                    // rad/s), which is enough to break synchronisation
                    // without ever making any star feel frantic.
                    starSpeeds[i] = 0.4 + _random.NextDouble() * 0.7;

                    starBaseOpacity[i] = baseOpacity;

                    // -----------------------------------------------------------------
                    // Glint creation for the brightest stars.
                    //
                    // The threshold is on baseOpacity, not on twinkle, so a
                    // star either always shows a glint at the peak of its
                    // cycle or never does. A star whose glint appeared on
                    // some cycles and not others would read as flickering;
                    // a star whose glint appears at every peak reads as
                    // sparkling.
                    //
                    // The glint's size is scaled by baseOpacity so a star
                    // that just clears the threshold gets a small ray and
                    // the brightest star in the field gets a full one. That
                    // gradient is what makes the field feel like a natural
                    // sky rather than a texture.
                    // -----------------------------------------------------------------
                    if (baseOpacity > 0.92)
                    {
                        // Base glint size 16–26px, then scaled by the star's
                        // own brightness (0.82–1.00 maps to 0.87–1.00 of
                        // the drawn size). The upper bound keeps the glint
                        // from ever exceeding the moon's radius, which would
                        // make the rays compete with the moon for attention.
                        double glintSize = (12.0 + _random.NextDouble() * 8.0)
                                         * (0.87 + baseOpacity * 0.13);

                        var glint = CreateStarGlintVisual(glintSize, starBrush);

                        // Soft outer glow so the rays read as light rather
                        // than as geometry. A DropShadowEffect with zero
                        // depth produces a symmetric halo around each ray.
                        glint.Effect = new DropShadowEffect
                        {
                            Color = Color.FromRgb(255, 210, 130),
                            BlurRadius = 4,
                            ShadowDepth = 0,
                            Opacity = 0.65
                        };

                        // Position: centre the glint on the star's centre.
                        // The star's top-left is at (starX, starY) and its
                        // size is sSize, so its centre is at
                        // (starX + sSize/2, starY + sSize/2). The glint
                        // has its own size, so its top-left goes at
                        // (centreX - glintSize/2, centreY - glintSize/2).
                        double centreX = starX + sSize / 2.0;
                        double centreY = starY + sSize / 2.0;
                        Canvas.SetLeft(glint, centreX - glintSize / 2.0);
                        Canvas.SetTop(glint, centreY - glintSize / 2.0);

                        skyCanvas.Children.Add(glint);
                        starGlints.Add(glint);
                    }
                    else
                    {
                        starGlints.Add(null);
                    }
                }

                rootGrid.Children.Add(skyCanvas);

                // Frame's own content sits in the middle of the z-stack.
                if (originalInner != null) rootGrid.Children.Add(originalInner);

                // Create transparent snow canvas that clips perfectly to the inside of the frame
                var canvas = new Canvas { IsHitTestVisible = false, ClipToBounds = true };
                rootGrid.Children.Add(canvas);

                // -----------------------------------------------------------------
                // NEW YEAR'S YEAR WATERMARK
                //
                // A faint gold "2026" (or whatever the current year is) in
                // the frame's bottom-right, sitting just above the resize
                // grip. This is the only place in the seasonal effect where
                // an explicit textual element appears, and it earns its
                // place by being the strongest possible year-specific cue
                // at almost zero cost.
                //
                // Deliberately very faint (alpha 110). The intent is for
                // the eye to *notice* the watermark before it reads it —
                // a bright year number would turn the frame into a
                // greeting card and break the "you are watching through a
                // window" illusion.
                //
                // Positioned with Canvas.Right/Bottom rather than
                // Canvas.Left/Top so it stays anchored to the frame's
                // corner as the frame resizes. The physics loop does not
                // need to reposition it.
                // -----------------------------------------------------------------
                TextBlock yearWatermark = null;
                if (_isNewYearActive)
                {
                    // -----------------------------------------------------------------
                    // HUGE YEAR WATERMARK
                    //
                    // A very large, very faint year number centered in the
                    // frame. This is the strongest year-specific cue in the
                    // effect and it earns its size by being the watermark,
                    // not an ornament: the eye should register the number
                    // before it reads it, which is exactly what a huge
                    // faint mark achieves where a small bright one does
                    // not.
                    //
                    // Positioning: because the parent is a Grid (rootGrid)
                    // and not a Canvas, the mark is placed with
                    // HorizontalAlignment/VerticalAlignment rather than
                    // Canvas.SetLeft/SetRight — those attached properties
                    // are silently ignored on a Grid child, which is why
                    // the previous 14px version may have appeared in the
                    // wrong place. Aligning to Center on both axes puts
                    // the number exactly in the middle of the frame at
                    // any size.
                    //
                    // Sizing: FontSize is computed from the frame's width
                    // (not its height) because the year is four digits
                    // wide and roughly one-tenth as tall as it is wide.
                    // 0.45 of the frame width gives a number that fills
                    // most of the frame horizontally with a comfortable
                    // margin on each side, and fits comfortably vertically
                    // on any frame taller than about 80 pixels. The
                    // physics loop recomputes this on every tick so a
                    // user-resized frame keeps a properly proportioned
                    // mark rather than one that becomes too big or too
                    // small.
                    //
                    // Colour: warm gold, but at an alpha of 55 out of 255
                    // — visible, unmistakably there, and yet clearly a
                    // background element that does not compete with the
                    // icons or the moon. The soft DropShadowEffect
                    // underneath gives the number a faint golden glow,
                    // which reads as "the mark is lit from behind" rather
                    // than "someone painted a number".
                    // -----------------------------------------------------------------
                    yearWatermark = new TextBlock
                    {
                        Text = DateTime.Now.Year.ToString(),
                        FontFamily = new FontFamily("Segoe UI"),
                        FontWeight = FontWeights.Bold,
                        Foreground = new SolidColorBrush(Color.FromArgb(55, 255, 225, 150)),
                        IsHitTestVisible = false,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        TextAlignment = TextAlignment.Center,
                        FontSize = Math.Max(24, frame.ActualWidth * 0.225),
                        Effect = new DropShadowEffect
                        {
                            Color = Color.FromRgb(255, 200, 120),
                            BlurRadius = 18,
                            ShadowDepth = 0,
                            Opacity = 0.45
                        }
                    };
                    rootGrid.Children.Add(yearWatermark);
                }

                // -----------------------------------------------------------------
                // GLITTER CANVAS
                //
                // A transparent full-frame canvas that hosts the year's
                // glitter sparkles. It is added AFTER the year watermark so
                // sparkles draw on top of the digits, and BEFORE the snow
                // canvas's siblings so it stays below snow and fireworks
                // (which fall in front of everything else). IsHitTestVisible
                // is false, so clicks pass straight through it to the icons
                // beneath.
                //
                // The canvas is populated and pruned by the physics loop.
                // Its only job at injection time is to exist in the visual
                // tree at the right z-order.
                // -----------------------------------------------------------------
                Canvas glitterCanvas = null;
                if (_isNewYearActive)
                {
                    glitterCanvas = new Canvas { IsHitTestVisible = false };
                    rootGrid.Children.Add(glitterCanvas);
                }
                // -----------------------------------------------------------------
                // GROUND AND ITS SURFACE DETAIL
                //
                // Three stacked polygons describe the snow surface:
                //   1. Ground      — the body of the snow, soft white, blurred
                //   2. Shadow      — a cool-blue band just below the crest, giving
                //                    the snow depth (the painter's cold-shadow trick)
                //   3. Crest line  — a sharp bright band exactly on the top edge,
                //                    giving the snow a defined contour
                //
                // All three share the same top-edge geometry, so they stay
                // aligned no matter how the accumulation drifts.
                // -----------------------------------------------------------------
                var ground = new Polygon
                {
                    Fill = Brushes.White,
                    Opacity = 0.85,
                    IsHitTestVisible = false,
                    Effect = new BlurEffect { Radius = 1.5 }
                };
                canvas.Children.Add(ground);

                var crestShadow = new Polygon
                {
                    // Cool blue in the recessed area under the crest. This is
                    // what makes snow read as *thick* rather than as a flat
                    // white sticker at the bottom of the frame.
                    Fill = new SolidColorBrush(Color.FromArgb(120, 155, 180, 220)),
                    IsHitTestVisible = false,
                    Effect = new BlurEffect { Radius = 2.0 }
                };
                canvas.Children.Add(crestShadow);

                var crestLine = new Polygon
                {
                    // Warm white on the lit top edge. The pairing of a warm
                    // crest and a cool shadow is the standard cinematic way
                    // to give a snow surface apparent volume.
                    Fill = new SolidColorBrush(Color.FromArgb(230, 255, 248, 235)),
                    IsHitTestVisible = false,
                    Effect = new BlurEffect { Radius = 0.8 }
                };
                canvas.Children.Add(crestLine);

                // Put the wrapped content back INSIDE the original Border
                mainBorder.Child = rootGrid;

                var ctx = new WinterContext
                {
                    Canvas = canvas,
                    SkyGradient = skyGradient,
                    HorizonGlow = horizonGlow,
                    YearWatermark = yearWatermark,
                    GlitterCanvas = glitterCanvas,
                    NextGlitterTime = _isNewYearActive
                        ? DateTime.Now.TimeOfDay.TotalSeconds + 0.5 + _random.NextDouble() * 0.5
                        : double.MaxValue,
                    NextFireworkTime = _isNewYearActive
                       ? DateTime.Now.TimeOfDay.TotalSeconds + 8 + _random.NextDouble() * 20
                       : double.MaxValue,
                    NextConfettiTime = _isNewYearActive
                       ? DateTime.Now.TimeOfDay.TotalSeconds + 3 + _random.NextDouble() * 6
                       : double.MaxValue,
                    Stars = stars,
                    StarPhases = starPhases,
                    StarBaseOpacity = starBaseOpacity,
                    StarSpeeds = starSpeeds,
                    StarGlints = starGlints,
                    // Moon fields. Every parallel array or visual reference
                    // declared on WinterContext must be assigned here, or the
                    // physics loop will throw on the first tick that reads
                    // it — the same omission that left StarSpeeds null when
                    // the per-star twinkle was introduced.
                    MoonCanvas = moonCanvas,
                    MoonHaze = moonHaze,
                    MoonCloudDrift = moonCloudDrift,
                    Ground = ground,
                    // First huge flake arrives 10–35 seconds after the frame
                    // is created, then roughly every 15–45 seconds thereafter
                    // (see the spawn check in WinterPhysicsLoop). The window
                    // is deliberately wide so that a set of five frames
                    // produces huge flakes spread across the minute rather
                    // than in a synchronised burst.
                    NextHugeFlakeTime = DateTime.Now.TimeOfDay.TotalSeconds + 10 + _random.NextDouble() * 25,
                    CrestLine = crestLine,
                    CrestShadow = crestShadow,
                    Segments = 30,
                    Accumulation = new double[30],
                    MaxAccumulation = new double[30]
                };

                // Pre-calculate hilly terrain with higher accumulation at the edges (wind drift effect)
                for (int i = 0; i < 30; i++)
                {
                    double distanceFromCenter = Math.Abs((i - 14.5) / 14.5);
                    double edgeBoost = Math.Pow(distanceFromCenter, 2) * 18.0;
                    double baseHeight = 6.0 + (_random.NextDouble() * 8.0);
                    ctx.MaxAccumulation[i] = baseHeight + edgeBoost;
                }
                _winterContexts[frame] = ctx;

                // Generate 15 snowflakes per frame. Each flake's visual is
                // created by RespawnSnowflake (via CreateSnowflakeVisual),
                // which also decides the depth band and shape — so the loop
                // below only has to attach the flake to the canvas and
                // stagger its initial Y position.
                for (int i = 0; i < 15; i++)
                {
                    var flake = new Snowflake { ParentWindow = frame };
                    RespawnSnowflake(flake);

                    // Scatter them vertically initially so they don't all fall
                    // from the exact top at startup.
                    flake.Y = _random.NextDouble() * Math.Max(50, frame.ActualHeight);
                    Canvas.SetTop(flake.Visual, flake.Y);

                    _snowflakes.Add(flake);
                }
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.UI, $"InterCore: Failed to inject snow into frame. {ex.Message}");
            }
        }

        /// <summary>
        /// Builds the visual for a snowflake. Returns any Shape-derived
        /// visual so the physics loop never has to branch on shape type —
        /// it only touches Width, Height, Opacity, and RenderTransform,
        /// all of which exist on Shape.
        ///
        /// The polygon shapes (hexagon, star) are defined in a unit square
        /// [0,1]x[0,1] with Stretch.Fill, so setting Width/Height on the
        /// returned Shape scales the crystalline pattern without any
        /// per-point recalculation. That lets the same visual serve the
        /// mid and near bands at any size.
        ///
        /// All shapes use a shared cool-white fill: pure white reads as
        /// "dots on glass", whereas a slight blue cast reads as "snow
        /// catching moonlight", which is what the effect is going for.
        /// </summary>
        private static Shape CreateSnowflakeVisual(FlakeShape shape, double size)
        {
            // Cool white: the eye reads a pure #FFFFFF dot as a cursor or
            // a stuck pixel. A whisper of blue makes it read as ice.
            var fill = new SolidColorBrush(Color.FromRgb(238, 245, 255));
            fill.Freeze();

            switch (shape)
            {
                case FlakeShape.Hexagon:
                    return new Polygon
                    {
                        Points = MakeSnowflakePoints(6, 1.0, 0.0),
                        Fill = fill,
                        Stretch = Stretch.Fill,
                        Width = size,
                        Height = size,
                        IsHitTestVisible = false
                    };

                case FlakeShape.Star:
                    // Six outer points, six inner valleys. A classic
                    // six-fold snowflake crystal, read at a glance even
                    // when the shape is only six or eight pixels wide.
                    return new Polygon
                    {
                        Points = MakeSnowflakePoints(6, 1.0, 0.42),
                        Fill = fill,
                        Stretch = Stretch.Fill,
                        Width = size,
                        Height = size,
                        IsHitTestVisible = false
                    };

                default:
                    return new Ellipse
                    {
                        Fill = fill,
                        Width = size,
                        Height = size,
                        IsHitTestVisible = false
                    };
            }
        }

        /// <summary>
        /// Convenience: find the winter canvas for a given frame. The
        /// canvas is the one on which all snow, bubbles, fireworks and
        /// confetti are drawn. Returning null when the frame is not
        /// currently themed keeps call sites simple.
        /// </summary>
        private static Canvas FindWinterCanvas(NonActivatingWindow window)
        {
            if (window == null) return null;
            return _winterContexts.TryGetValue(window, out var ctx) ? ctx.Canvas : null;
        }

        /// <summary>
        /// Launches a firework comet. The comet starts below the frame's
        /// bottom edge and rises to an apex that is randomly chosen in
        /// the upper 60% of the frame. The apex range avoids both the
        /// very top (where the sky gradient and stars live — a burst up
        /// there would fight them for attention) and the very middle
        /// (where it would be lost against the icons). The sweet spot is
        /// roughly the top quarter to top half of the frame, which is
        /// also where a distant firework would appear.
        ///
        /// The burst palette is chosen here rather than at burst time so
        /// the comet's own colour matches the dominant burst colour.
        /// Four palettes are drawn from a fixed set, which gives the set
        /// of frames on a desktop a coherent look — they all celebrate
        /// with the same four colour families — without repeating.
        /// </summary>
        private static void SpawnFirework(NonActivatingWindow window)
        {
            var canvas = FindWinterCanvas(window);
            if (canvas == null) return;

            var palette = new (Color Main, Color Accent)[]
            {
                (Color.FromRgb(255, 215, 130), Color.FromRgb(255, 245, 220)), // Gold / white
                (Color.FromRgb(230, 130, 160), Color.FromRgb(255, 200, 220)), // Rose
                (Color.FromRgb( 90, 190, 200), Color.FromRgb(200, 240, 250)), // Teal
                (Color.FromRgb(210, 210, 230), Color.FromRgb(255, 250, 240))  // Silver
            };
            var pick = palette[_random.Next(palette.Length)];

            double frameW = Math.Max(40, window.ActualWidth);
            double frameH = Math.Max(60, window.ActualHeight);
            double cometSize = 5.0;

            var cometVisual = new Ellipse
            {
                Width = cometSize,
                Height = cometSize,
                Fill = new SolidColorBrush(pick.Main),
                IsHitTestVisible = false,
                Effect = new DropShadowEffect
                {
                    Color = pick.Main,
                    BlurRadius = 12,
                    ShadowDepth = 0,
                    Opacity = 0.9
                }
            };

            var comet = new FireworkComet
            {
                Visual = cometVisual,
                ParentWindow = window,
                BaseX = frameW * (0.20 + _random.NextDouble() * 0.60),
                Y = frameH + 10,
                ApexY = frameH * (0.15 + _random.NextDouble() * 0.35),
                Speed = 2.4 + _random.NextDouble() * 1.2,
                BurstColor = pick.Main,
                BurstAccent = pick.Accent,
                SwayPhase = _random.NextDouble() * Math.PI * 2,
                SwayAmplitude = 4 + _random.NextDouble() * 6
            };

            Canvas.SetLeft(cometVisual, comet.BaseX);
            Canvas.SetTop(cometVisual, comet.Y);
            canvas.Children.Add(cometVisual);
            _fireworkComets.Add(comet);
        }

        /// <summary>
        /// Bursts a firework comet into sparks. Spark count and initial
        /// velocity are tuned to produce a burst that reads as a single
        /// coherent event: enough sparks to fill a small area, few enough
        /// that the individual points are visible. The 22–38 range is
        /// where that balance sits at the sizes and frame dimensions this
        /// effect runs at.
        ///
        /// Sparks are added to the same canvas as everything else and
        /// tracked in _fireworkSparks until their life expires. Colour is
        /// drawn per spark from the comet's two-colour palette, with the
        /// dominant colour roughly twice as likely as the accent — which
        /// produces the characteristic "gold burst with white sparks"
        /// look rather than an even mix, which reads as noise.
        /// </summary>
        private static void BurstFirework(FireworkComet comet)
        {
            var canvas = FindWinterCanvas(comet.ParentWindow);
            if (canvas == null) return;

            int sparkCount = 22 + _random.Next(17);
            for (int i = 0; i < sparkCount; i++)
            {
                double angle = (2 * Math.PI * i) / sparkCount + _random.NextDouble() * 0.3;
                double speed = 1.2 + _random.NextDouble() * 1.8;
                double sparkSize = 2.0 + _random.NextDouble() * 1.5;

                // Dominant vs accent: 2:1 weighting reads as a gold burst
                // with silver glints, rather than as an even sprinkle.
                bool useAccent = _random.NextDouble() < 0.33;
                var sparkColor = useAccent ? comet.BurstAccent : comet.BurstColor;

                var sparkVisual = new Ellipse
                {
                    Width = sparkSize,
                    Height = sparkSize,
                    Fill = new SolidColorBrush(sparkColor),
                    IsHitTestVisible = false,
                    Effect = new DropShadowEffect
                    {
                        Color = sparkColor,
                        BlurRadius = 4,
                        ShadowDepth = 0,
                        Opacity = 0.7
                    }
                };

                var spark = new FireworkSpark
                {
                    Visual = sparkVisual,
                    ParentWindow = comet.ParentWindow,
                    X = comet.BaseX,
                    Y = comet.ApexY,
                    VX = Math.Cos(angle) * speed,
                    VY = Math.Sin(angle) * speed,
                    Life = 1.8 + _random.NextDouble() * 0.8,
                    MaxLife = 0 // assigned below so Life and MaxLife match
                };
                spark.MaxLife = spark.Life;

                Canvas.SetLeft(sparkVisual, spark.X);
                Canvas.SetTop(sparkVisual, spark.Y);
                canvas.Children.Add(sparkVisual);
                _fireworkSparks.Add(spark);
            }
        }

        /// <summary>
        /// Spawns a single piece of confetti at the top of the frame.
        /// Colours are drawn from a small palette chosen for the effect:
        /// deep rose, teal, gold, and silver — festive but muted enough
        /// not to compete with the fireworks. Size (3×6 px) and fall
        /// speed (0.5–0.9 px/tick) sit between the mid and near snowflake
        /// bands, so the confetti reads as falling through the same air
        /// as the snow rather than at a different altitude.
        /// </summary>
        private static void SpawnConfetti(NonActivatingWindow window)
        {
            var canvas = FindWinterCanvas(window);
            if (canvas == null) return;

            var palette = new[]
            {
                Color.FromRgb(230, 130, 160), // Rose
                Color.FromRgb( 90, 190, 200), // Teal
                Color.FromRgb(230, 190,  90), // Gold
                Color.FromRgb(200, 205, 220)  // Silver
            };
            var color = palette[_random.Next(palette.Length)];

            const double confW = 3.0;
            const double confH = 6.0;

            var visual = new Rectangle
            {
                Width = confW,
                Height = confH,
                Fill = new SolidColorBrush(color),
                IsHitTestVisible = false,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new RotateTransform(0)
            };

            var conf = new Confetti
            {
                Visual = visual,
                ParentWindow = window,
                BaseX = _random.NextDouble() * Math.Max(40, window.ActualWidth),
                Y = -10,
                Speed = 0.5 + _random.NextDouble() * 0.4,
                DriftOffset = 8 + _random.NextDouble() * 10,
                DriftSpeed = 0.4 + _random.NextDouble() * 0.8,
                RotationPhase = _random.NextDouble() * Math.PI * 2,
                RotationSpeed = (0.8 + _random.NextDouble() * 1.6) *
                                 (_random.NextDouble() < 0.5 ? -1.0 : 1.0),
                Life = 22 + _random.NextDouble() * 12,
                MaxLife = 0
            };
            conf.MaxLife = conf.Life;

            Canvas.SetLeft(visual, conf.BaseX);
            Canvas.SetTop(visual, conf.Y);
            canvas.Children.Add(visual);
            _confetti.Add(conf);
        }

        /// <summary>
        /// Builds a realistic snowflake as a stroked Path. Unlike the flat
        /// hexagon and star polygons used for small flakes, this is a
        /// skeleton: six arms radiating from the centre, each carrying two
        /// pairs of side branches.


        /// <summary>
        /// Builds a realistic snowflake as a stroked Path. Unlike the flat
        /// hexagon and star polygons used for small flakes, this is a
        /// skeleton: six arms radiating from the centre, each carrying two
        /// pairs of side branches. At 18px and above the crystalline
        /// structure resolves clearly, and the silhouette reads as a
        /// snowflake rather than as a shape.
        ///
        /// Design notes:
        ///   - Arms are at 60° intervals (six-fold symmetry, which is what
        ///     ice crystals actually have).
        ///   - Each arm carries two pairs of branches at t = 0.35 and
        ///     t = 0.62 of the arm length. Earlier branches are longer,
        ///     which is the natural taper a real snowflake shows — the
        ///     fern grows outward, not inward.
        ///   - Branches diverge at 60° from the arm direction, matching
        ///     the hexagonal lattice. Using 45° or 90° would look arbitrary;
        ///     60° is what the eye reads as "correct" without being able
        ///     to say why.
        ///   - Strokes are capped with rounded ends, which is what gives
        ///     the tips a "soft ice" read instead of a machined look.
        ///   - Stroke thickness scales with size (5.5% of the diameter)
        ///     so a 20px flake and a 30px flake have the same proportions
        ///     rather than the same absolute line weight.
        ///
        /// Returns a Path, which is a Shape, so every existing call site
        /// that manipulates Width, Height, Opacity, RenderTransform and
        /// RenderTransformOrigin continues to work without modification.
        /// </summary>
        private static Shape CreateIntricateSnowflakeVisual(double size)
        {
            double centre = size / 2.0;

            // Leave a small inset so the stroke thickness does not push
            // the outer arm tips past the shape's own Width/Height — a
            // Path with StrokeWidth 1.5 on an arm that ends exactly at the
            // bounding edge would render half its stroke outside the box,
            // producing a flake that looks clipped against the frame's
            // layout grid even though it is not.
            double strokeThickness = Math.Max(1.0, size * 0.055);
            double armRadius = centre - strokeThickness * 0.7;

            // Cool-white stroke. Matches the fill of small flakes so the
            // depth layering reads coherently — a huge flake and a small
            // flake in the same frame are clearly the same material at
            // different distances.
            var strokeBrush = new SolidColorBrush(Color.FromRgb(240, 248, 255));
            strokeBrush.Freeze();

            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                for (int arm = 0; arm < 6; arm++)
                {
                    // Six-fold symmetry. The -π/2 offset puts the first arm
                    // straight up, which reads more like a snowflake than
                    // an arm pointing right does.
                    double angle = arm * Math.PI / 3.0 - Math.PI / 2.0;
                    double armTipX = centre + Math.Cos(angle) * armRadius;
                    double armTipY = centre + Math.Sin(angle) * armRadius;

                    // Main spine of this arm, from the centre to the tip.
                    ctx.BeginFigure(new Point(centre, centre), false, false);
                    ctx.LineTo(new Point(armTipX, armTipY), true, false);

                    // Two pairs of side branches. The first pair (t = 0.35)
                    // is longer and fuller; the second (t = 0.62) is
                    // shorter. This asymmetry is what makes a snowflake
                    // read as "grown" rather than "drawn".
                    double[] branchPositions = { 0.35, 0.62 };
                    for (int bi = 0; bi < branchPositions.Length; bi++)
                    {
                        double t = branchPositions[bi];

                        // Base point of the branch on the arm's spine.
                        double baseX = centre + Math.Cos(angle) * armRadius * t;
                        double baseY = centre + Math.Sin(angle) * armRadius * t;

                        // Branch length: proportional to the remaining
                        // arm length past this point, times 0.5 so the
                        // branches stay inside the flake's silhouette.
                        double branchLength = armRadius * (1.0 - t) * 0.5;

                        // Two branches per pair, on opposite sides of the
                        // spine, each at 60° from the spine direction —
                        // which is to say, along the neighbouring arms'
                        // directions.
                        for (int side = -1; side <= 1; side += 2)
                        {
                            double bAngle = angle + side * Math.PI / 3.0;
                            double tipX = baseX + Math.Cos(bAngle) * branchLength;
                            double tipY = baseY + Math.Sin(bAngle) * branchLength;

                            ctx.BeginFigure(new Point(baseX, baseY), false, false);
                            ctx.LineTo(new Point(tipX, tipY), true, false);
                        }
                    }
                }
            }
            geometry.Freeze();

            return new System.Windows.Shapes.Path
            {
                Data = geometry,
                Stroke = strokeBrush,
                StrokeThickness = strokeThickness,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                Width = size,
                Height = size,
                IsHitTestVisible = false
            };
        }

        /// <summary>
        /// Builds a four-point star glint: two crossed rays, each tapering
        /// from full width at the centre to a point at the tip. This is the
        /// shape almost every illustrated star carries and it is what the
        /// eye reads as "this star is shining". Without it, a bright star
        /// is just a bright dot.
        ///
        /// The shape is drawn in positive coordinates [0, size] so that
        /// placing it on a Canvas at (starX + starSize/2 - glintSize/2,
        /// starY + starSize/2 - glintSize/2) puts its centre exactly on
        /// the star's centre.
        ///
        /// The waist — the half-width of the rays at the centre — is
        /// deliberately small (12% of the glint's size). Bigger waists
        /// make the glint look like a plus sign; smaller waists make it
        /// look like a thin cross. Twelve percent is where the ray reads
        /// as a ray of light rather than as a graphic.
        ///
        /// Returns a Path so that the caller can attach a DropShadowEffect
        /// for a soft halo without needing a wrapper element.
        /// </summary>
        private static Shape CreateStarGlintVisual(double size, SolidColorBrush brush)
        {
            double cx = size / 2.0;
            double cy = size / 2.0;
            double waist = size * 0.12;

            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                // Right ray: base wide, tip pointed.
                ctx.BeginFigure(new Point(cx, cy - waist), true, true);
                ctx.LineTo(new Point(size, cy), true, false);
                ctx.LineTo(new Point(cx, cy + waist), true, false);

                // Left ray.
                ctx.BeginFigure(new Point(cx, cy - waist), true, true);
                ctx.LineTo(new Point(0, cy), true, false);
                ctx.LineTo(new Point(cx, cy + waist), true, false);

                // Down ray.
                ctx.BeginFigure(new Point(cx - waist, cy), true, true);
                ctx.LineTo(new Point(cx, size), true, false);
                ctx.LineTo(new Point(cx + waist, cy), true, false);

                // Up ray.
                ctx.BeginFigure(new Point(cx - waist, cy), true, true);
                ctx.LineTo(new Point(cx, 0), true, false);
                ctx.LineTo(new Point(cx + waist, cy), true, false);
            }
            geometry.Freeze();

            return new System.Windows.Shapes.Path
            {
                Data = geometry,
                Fill = brush,
                IsHitTestVisible = false,
                // Starts invisible; the physics loop will raise its opacity
                // only at the peak of the star's twinkle cycle. This
                // prevents a glint from flashing on for a frame before the
                // first tick fires, which would read as a glitch.
                Opacity = 0
            };
        }

        /// <summary>
        /// Builds a snowflake crystal's point collection. <paramref name="arms"/>
        /// is the number of outer points, <paramref name="outerRadius"/> and
        /// <paramref name="innerRadius"/> define the crest and valley radii
        /// as fractions of the unit square's half-extent. Passing innerRadius
        /// as zero yields a plain polygon (the hexagon case); passing a
        /// positive innerRadius alternates outer points with valley points
        /// to produce a star.
        ///
        /// All coordinates are in [0,1] so that Stretch.Fill on the returned
        /// Shape maps them directly to the shape's Width and Height.
        /// </summary>
        private static PointCollection MakeSnowflakePoints(int arms, double outerRadius, double innerRadius)
        {
            var points = new PointCollection();
            int total = innerRadius > 0 ? arms * 2 : arms;
            const double centre = 0.5;

            for (int i = 0; i < total; i++)
            {
                // Alternate outer / inner on star shapes; use the outer
                // radius on every step for a plain polygon.
                double radius = (innerRadius > 0 && i % 2 == 1) ? innerRadius : outerRadius;

                // Offset by 90° so the first point of a hexagon sits at the
                // top, which reads more like a snowflake crystal than a flat
                // top and bottom.
                double angle = (Math.PI * 2 * i / total) - (Math.PI / 2);

                points.Add(new Point(
                    centre + Math.Cos(angle) * radius * centre,
                    centre + Math.Sin(angle) * radius * centre));
            }
            return points;
        }

        private static void RespawnSnowflake(Snowflake flake)
        {
            flake.IsMelting = false;

            // -----------------------------------------------------------------
            // HUGE FLAKE PATH
            //
            // Huge flakes bypass the depth-band selection entirely. They are
            // a distinct visual category — the "hero flake" that drifts
            // past the window once every half minute — and giving them the
            // same parameter clusters as regular flakes would make them
            // read as "a bug in the size code" rather than as a feature.
            //
            // The parameter choices below are tuned for the specific
            // visual job huge flakes have:
            //
            //   Size 18–30px    — big enough to see the crystalline
            //                     silhouette clearly, small enough not to
            //                     obscure the icons for long.
            //   Speed 0.9–1.4   — slower than near regular flakes (1.2–2.1)
            //                     so each huge flake lingers in view for
            //                     about four seconds, which is what makes
            //                     it feel like a "moment" rather than a
            //                     blip.
            //   Blur 2.5        — much softer than any regular flake, which
            //                     sells the "close to the eye" impression.
            //                     A sharp huge flake reads as a shape; a
            //                     blurred one reads as a snowflake in
            //                     flight.
            //   Opacity 0.72    — high enough to be unmistakably in front
            //                     of the icons, low enough to still be
            //                     translucent (real snow is never fully
            //                     opaque).
            //   Rotation 0.55   — visibly tumbling, because the eye tracks
            //                     a big flake longer and will notice if it
            //                     falls without rotating.
            // -----------------------------------------------------------------
            if (flake.IsHuge)
            {
                flake.Size = 18.0 + _random.NextDouble() * 12.0;   // 18–30 px
                flake.Speed = 0.9 + _random.NextDouble() * 0.5;    // slow drift
                flake.DriftOffset = 12.0 + _random.NextDouble() * 10.0;
                flake.DriftSpeed = 0.5 + _random.NextDouble() * 0.6;
                flake.MeltRate = 0.015; // slow fade, gives it a graceful exit
                flake.RotationPhase = _random.NextDouble() * Math.PI * 2;
                flake.RotationSpeed = (0.45 + _random.NextDouble() * 0.25) *
                                      (_random.NextDouble() < 0.5 ? -1.0 : 1.0);

                // Real snowflake geometry, not a flat polygon.
                //
                // At 18–30px the crystalline structure of a snowflake is
                // what the eye expects to see: six arms, side branches,
                // a fern-like silhouette. A filled hexagon or star at
                // that size reads as a shape rather than as ice. Small
                // flakes keep their polygon fill because below ~8px a
                // crystalline skeleton is too fine to resolve.
                //
                // The Effect is now a DropShadowEffect with ShadowDepth 0
                // instead of a BlurEffect. A BlurEffect smears the strokes
                // together at large sizes — the six arms become one bright
                // blob, which is the exact opposite of what the shape is
                // for. A DropShadowEffect with zero depth produces a
                // symmetric glow around each stroke, which reads as "a
                // crystal catching light" and keeps the arms distinct.
                //
                // The locals here are named with a "huge" prefix because
                // C# forbids a nested block from declaring a variable whose
                // name matches one declared anywhere else in the enclosing
                // method — even if that outer declaration comes later in
                // the source. The regular flake path below uses "shape",
                // "newVisual" and "width"; using the same names here would
                // be a compile error.
                var hugeVisual = CreateIntricateSnowflakeVisual(flake.Size);
                hugeVisual.Opacity = 0.72;
                hugeVisual.Effect = new DropShadowEffect
                {
                    Color = Color.FromRgb(220, 240, 255),
                    BlurRadius = Math.Max(3.0, flake.Size * 0.20),
                    ShadowDepth = 0,
                    Opacity = 0.55
                };
                hugeVisual.RenderTransformOrigin = new Point(0.5, 0.5);
                hugeVisual.RenderTransform = new RotateTransform(0);

                var hugeCtx = _winterContexts.TryGetValue(flake.ParentWindow, out var hc) ? hc : null;
                if (hugeCtx != null)
                {
                    // Added to the same canvas as regular flakes. That canvas
                    // is IsHitTestVisible = false, so huge flakes appear in
                    // front of the icons visually but never intercept a
                    // single mouse event — the click goes straight through
                    // to whatever icon is behind the flake.
                    hugeCtx.Canvas.Children.Add(hugeVisual);
                }

                flake.Visual = hugeVisual;
                flake.Y = -40;
                double hugeWidth = Math.Max(100, flake.ParentWindow.ActualWidth);
                flake.BaseX = _random.NextDouble() * hugeWidth;

                Canvas.SetLeft(flake.Visual, flake.BaseX);
                Canvas.SetTop(flake.Visual, flake.Y);
                return;
            }

            // -----------------------------------------------------------------
            // DEPTH BAND SELECTION
            //
            // Real snow reads as three overlapping planes: a faint, slow,
            // sharp background; a mid plane of medium flakes; and a large,
            // blurred, fast foreground. The current single-plane effect reads
            // as a texture rather than as a scene because every flake obeys
            // the same parameters.
            //
            // Here each flake picks a band at spawn and derives all of its
            // visual parameters from that band. The band persists across
            // respawns (see the Snowflake.Depth field), so a near flake
            // stays near for its lifetime instead of hopping depth bands on
            // every melt cycle. Over a few seconds the three bands settle
            // into a stable-looking field that reads as genuine snowfall.
            // -----------------------------------------------------------------
            double bandPick = _random.NextDouble();
            if (bandPick < 0.5) flake.Depth = FlakeDepth.Far;
            else if (bandPick < 0.85) flake.Depth = FlakeDepth.Mid;
            else flake.Depth = FlakeDepth.Near;

            // Shape selection. Small far flakes are too tiny for a
            // crystalline silhouette to resolve, so they are almost always
            // circles. Near flakes are large enough that a hexagon or star
            // reads as a snowflake rather than as a smudge.
            double shapePick = _random.NextDouble();
            FlakeShape shape;
            if (flake.Depth == FlakeDepth.Far)
                shape = shapePick < 0.80 ? FlakeShape.Circle : FlakeShape.Hexagon;
            else if (flake.Depth == FlakeDepth.Mid)
                shape = shapePick < 0.40 ? FlakeShape.Circle : (shapePick < 0.80 ? FlakeShape.Hexagon : FlakeShape.Star);
            else
                shape = shapePick < 0.20 ? FlakeShape.Circle : (shapePick < 0.60 ? FlakeShape.Hexagon : FlakeShape.Star);

            // Size, opacity, speed, sway — all keyed to the depth band.
            switch (flake.Depth)
            {
                case FlakeDepth.Far:
                    flake.Size = 2.0 + _random.NextDouble() * 1.0;   // 2–3 px
                    flake.Speed = 0.35 + _random.NextDouble() * 0.35; // slow
                    flake.DriftOffset = 3.0 + _random.NextDouble() * 4.0;
                    flake.DriftSpeed = 0.3 + _random.NextDouble() * 0.5;
                    break;
                case FlakeDepth.Mid:
                    flake.Size = 3.5 + _random.NextDouble() * 1.5;   // 3.5–5 px
                    flake.Speed = 0.7 + _random.NextDouble() * 0.5;
                    flake.DriftOffset = 5.0 + _random.NextDouble() * 6.0;
                    flake.DriftSpeed = 0.5 + _random.NextDouble() * 0.8;
                    break;
                default: // Near
                    flake.Size = 5.5 + _random.NextDouble() * 2.5;   // 5.5–8 px
                    flake.Speed = 1.2 + _random.NextDouble() * 0.9;
                    flake.DriftOffset = 8.0 + _random.NextDouble() * 10.0;
                    flake.DriftSpeed = 0.7 + _random.NextDouble() * 1.1;
                    break;
            }

            // -----------------------------------------------------------------
            // VISUAL RECREATION
            //
            // The visual has to be replaced on every respawn because the
            // flake may have picked a new shape and a new size. Removing the
            // old visual and adding the new one keeps the canvas child list
            // tidy and places near flakes at the top of the z-stack (they
            // were added most recently), which is what we want: a near flake
            // should occlude a far flake behind it.
            // -----------------------------------------------------------------
            var ctx = _winterContexts.TryGetValue(flake.ParentWindow, out var parentCtx) ? parentCtx : null;
            if (flake.Visual != null && ctx != null)
            {
                ctx.Canvas.Children.Remove(flake.Visual);
            }

            var newVisual = CreateSnowflakeVisual(shape, flake.Size);
            flake.Visual = newVisual;

            // Depth-based opacity. Far flakes are dim enough to read as
            // "behind several kilometres of air"; near flakes are bright.
            // The pairing with size gives the eye an unambiguous depth cue.
            double baseOpacity;
            switch (flake.Depth)
            {
                case FlakeDepth.Far: baseOpacity = 0.32 + _random.NextDouble() * 0.20; break;
                case FlakeDepth.Mid: baseOpacity = 0.55 + _random.NextDouble() * 0.20; break;
                default: baseOpacity = 0.78 + _random.NextDouble() * 0.20; break;
            }
            newVisual.Opacity = baseOpacity;

            // Small blur only on the near band, where it reads as motion
            // blur. Far and mid flakes stay crisp, because softness in the
            // distance reads as smudge rather than as depth.
            if (flake.Depth == FlakeDepth.Near)
            {
                newVisual.Effect = new BlurEffect { Radius = 0.6 };
            }

            // Slow rotation. Near flakes tumble more visibly than far flakes
            // (they cross more of the eye's attention), but the rate stays
            // deliberately gentle on every band so the effect never reads as
            // spinning. Rotation is applied per-tick by the physics loop.
            flake.RotationPhase = _random.NextDouble() * Math.PI * 2;
            flake.RotationSpeed = (flake.Depth == FlakeDepth.Near ? 0.35 : 0.18) *
                                  (_random.NextDouble() < 0.5 ? -1.0 : 1.0);

            if (newVisual != null)
            {
                newVisual.RenderTransformOrigin = new Point(0.5, 0.5);
                newVisual.RenderTransform = new RotateTransform(0);
            }

            if (ctx != null && newVisual != null)
            {
                ctx.Canvas.Children.Add(newVisual);
            }

            flake.Y = -10;
            double width = Math.Max(100, flake.ParentWindow.ActualWidth);
            flake.BaseX = _random.NextDouble() * width;

            if (flake.Visual != null)
            {
                Canvas.SetLeft(flake.Visual, flake.BaseX);
                Canvas.SetTop(flake.Visual, flake.Y);
            }

            flake.MeltRate = _random.NextDouble() * 0.02 + 0.01;
        }

        #endregion

        #region Seasonal Easter Eggs (Halloween Theme)

        private static void ActivateHalloweenTheme()
        {
            if (_isHalloweenActive) return;
            _isHalloweenActive = true;

            LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.UI, "InterCore: Halloween Theme activated!");

            // Run physics loop at ~30 FPS
            _halloweenTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
            _halloweenTimer.Tick += HalloweenPhysicsLoop;
            _halloweenTimer.Start();
        }

        private static void HalloweenPhysicsLoop(object sender, EventArgs e)
        {
            _halloweenTickCounter++;
            var time = DateTime.Now.TimeOfDay.TotalSeconds;

            // 1. Scan for newly created frames
            if (_halloweenTickCounter >= 60)
            {
                _halloweenTickCounter = 0;
                var currentFrames = Application.Current.Windows.OfType<NonActivatingWindow>().ToList();
                foreach (var frame in currentFrames)
                {
                    if (frame.IsLoaded && !_halloweenContexts.ContainsKey(frame))
                    {
                        InjectHalloweenCanvas(frame);
                    }
                }
            }

            // 2. Update geometry, mist, flicker, and hue rotation
            foreach (var kvp in _halloweenContexts)
            {
                var window = kvp.Key;
                var ctx = kvp.Value;
                if (window == null || !window.IsLoaded) continue;

                double w = Math.Max(10, window.ActualWidth);
                double h = Math.Max(10, window.ActualHeight);

                // --- Sky gradient tracks the frame size ---
                ctx.SkyGradient.Width = w;
                ctx.SkyGradient.Height = Math.Max(60, h * 0.40);

                // --- Moon positioning ---
                //
                // The moon Canvas is 100x100 with the disc centred at (50,50).
                // Anchoring it at (w - 130, 20) puts the disc centre at
                // (w - 80, 70) in frame coordinates: upper-right, roughly
                // 80px in from the right edge and 70px down from the top.
                //
                // That placement is chosen so the moon:
                //   - sits below the title bar, so it never fights the title
                //     text or the lock icon for attention,
                //   - is far enough from the right edge that a narrow frame
                //     does not clip the halo into an obvious half-circle,
                //   - and stays visually consistent across frames of very
                //     different widths (the disc is always 80px in from the
                //     right edge, not a fraction of the frame width), which
                //     is what makes the moons in a stacked set look like the
                //     same moon seen through different windows rather than
                //     like a resizing stamp.
                Canvas.SetLeft(ctx.MoonCanvas, w - 130);
                Canvas.SetTop(ctx.MoonCanvas, 20);

                // --- Slow cloud drift across the moon ---
                //
                // A very slow horizontal sweep, ±6px on a ~40-second cycle.
                // This is deliberately far slower than anything else in the
                // scene: the mist and the embers move on human timescales,
                // but cloud motion at this distance should be almost
                // subliminal. If the observer can see the cloud moving, it
                // is moving too fast.
                if (ctx.MoonCloudDrift != null)
                {
                    ctx.MoonCloudDrift.X = Math.Sin(time * 0.16) * 6.0;
                }

                // --- Back mist ---
                ctx.BackMist.Width = w;
                Canvas.SetTop(ctx.BackMist, h - 120);

                // --- Mid mist: vertical wave + slow horizontal drift ---
                //
                // Drift is applied through RenderTransform.X rather than baked
                // into the point x-coordinates, so the wave shape can be
                // regenerated every tick without the drift compound-multiplying
                // across frames. The polygon deliberately extends ~80px past
                // each canvas edge so a ±40px drift never opens a gap.
                double midDrift = Math.Sin(time * 0.18) * 40.0;
                double midSegWidth = (w + 160) / (ctx.MidMistSegments - 1);
                var midPoints = new PointCollection();
                midPoints.Add(new Point(w + 80, h));
                midPoints.Add(new Point(-80, h));
                for (int i = 0; i < ctx.MidMistSegments; i++)
                {
                    double x = -80 + i * midSegWidth;
                    double waveY = 95 + Math.Sin((time * 0.35) + ctx.MidMistPhases[i]) * 22.0;
                    midPoints.Add(new Point(x, h - waveY));
                }
                ctx.MidMist.Points = midPoints;
                if (ctx.MidMist.RenderTransform is TranslateTransform midT) midT.X = midDrift;

                // --- Pumpkins: half-buried in the fog ---
                // Anchor so roughly half of each pumpkin sits inside the
                // denser part of the front mist. The glowing face and the
                // stem still poke out above the fog line, which is what
                // produces the "peeking out of the mist" read.
                Canvas.SetLeft(ctx.BigPumpkin, w - 85);
                Canvas.SetTop(ctx.BigPumpkin, h - 36);

                Canvas.SetLeft(ctx.SmallPumpkin, w - 40);
                Canvas.SetTop(ctx.SmallPumpkin, h - 24);

                // --- Candle flicker on the pumpkins ---
                // Two incommensurate sine frequencies plus a per-pumpkin phase
                // gives a jittery, fire-like pulse rather than a smooth throb.
                // The two pumpkins use independent phases, so they never pulse
                // in lockstep.
                double bigFlicker = 0.85
                                  + Math.Sin(time * 7.3 + ctx.BigPumpkinFlickerPhase) * 0.10
                                  + Math.Sin(time * 13.1 + ctx.BigPumpkinFlickerPhase * 2.3) * 0.05;
                double smallFlicker = 0.85
                                    + Math.Sin(time * 8.1 + ctx.SmallPumpkinFlickerPhase) * 0.10
                                    + Math.Sin(time * 14.7 + ctx.SmallPumpkinFlickerPhase * 1.7) * 0.05;

                ctx.BigPumpkinFaceGlow.Opacity = Math.Clamp(bigFlicker, 0.60, 1.0);
                ctx.SmallPumpkinFaceGlow.Opacity = Math.Clamp(smallFlicker, 0.60, 1.0);

                // The ambient halo flickers with the face, but with a much
                // smaller amplitude — a large swing would make the whole
                // pumpkin change size to the eye.
                if (ctx.BigPumpkin.Effect is DropShadowEffect bigOuter)
                    bigOuter.Opacity = Math.Clamp(0.55 + (bigFlicker - 0.85) * 1.2, 0.35, 0.85);
                if (ctx.SmallPumpkin.Effect is DropShadowEffect smallOuter)
                    smallOuter.Opacity = Math.Clamp(0.55 + (smallFlicker - 0.85) * 1.2, 0.35, 0.85);

                // --- Occasional hue rotation ---
                // Fires every 8–20 seconds per pumpkin, independently, and
                // cross-fades over 0.8s so the change reads as "the fire
                // shifted colour" rather than "someone pressed a button".
                if (time >= ctx.BigPumpkinNextColorChange)
                {
                    int next = PickPumpkinColorIndex(ctx.BigPumpkinColorIndex);
                    Color target = PumpkinFacePalette[next].Color;

                    // Animate both the fill and the glow toward the new hue.
                    // Changing only one would break the "light behind the
                    // face" illusion and make the pumpkin look like two
                    // separate objects.
                    ctx.BigPumpkinFaceGlow.BeginAnimation(
                        DropShadowEffect.ColorProperty,
                        new ColorAnimation
                        {
                            To = target,
                            Duration = TimeSpan.FromSeconds(0.8),
                            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
                        });

                    if (ctx.BigPumpkin is Canvas bigCanvas)
                    {
                        var faceFill = FindFaceFill(bigCanvas);
                        if (faceFill != null) faceFill.Color = target;
                    }

                    ctx.BigPumpkinColorIndex = next;
                    ctx.BigPumpkinNextColorChange = time + _random.NextDouble() * 12 + 8;
                }

                if (time >= ctx.SmallPumpkinNextColorChange)
                {
                    int next = PickPumpkinColorIndex(ctx.SmallPumpkinColorIndex);
                    Color target = PumpkinFacePalette[next].Color;

                    ctx.SmallPumpkinFaceGlow.BeginAnimation(
                        DropShadowEffect.ColorProperty,
                        new ColorAnimation
                        {
                            To = target,
                            Duration = TimeSpan.FromSeconds(0.8),
                            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
                        });

                    if (ctx.SmallPumpkin is Canvas smallCanvas)
                    {
                        var faceFill = FindFaceFill(smallCanvas);
                        if (faceFill != null) faceFill.Color = target;
                    }

                    ctx.SmallPumpkinColorIndex = next;
                    ctx.SmallPumpkinNextColorChange = time + _random.NextDouble() * 12 + 8;
                }

                // --- Front mist (unchanged behaviour) ---
                var points = new PointCollection();
                points.Add(new Point(w, h));
                points.Add(new Point(0, h));

                double segWidth = w / (ctx.Segments - 1);
                for (int i = 0; i < ctx.Segments; i++)
                {
                    double waveY = 80 + Math.Sin((time * 0.6) + ctx.MistPhases[i]) * 25.0;
                    points.Add(new Point(i * segWidth, h - waveY));
                }
                ctx.FrontMist.Points = points;

                // --- Zigzag ridge layers ---
                //
                // Each ridge mirrors its parent mist's contour at reduced
                // amplitude and sits a fixed offset above it. See the
                // UpdateRidge helper for the phase mapping. The three calls
                // below encode the visual hierarchy: the front ridge is
                // widest, sharpest and lowest; the mid ridge is thinner and
                // higher; the high ridge is the thinnest and highest, using
                // its own phases because it has no parent mist to derive
                // from.
                UpdateRidge(ctx.FrontRidge, ctx.Segments, ctx.MistPhases,
                    time, frequency: 0.6, amplitude: 22, baseHeight: 100,
                    thickness: 12, w: w, h: h);

                UpdateRidge(ctx.MidRidge, ctx.MidMistSegments, ctx.MidMistPhases,
                    time, frequency: 0.35, amplitude: 19, baseHeight: 122,
                    thickness: 10, w: w, h: h);

                UpdateRidge(ctx.HighRidge, ctx.HighRidgePhases.Length, ctx.HighRidgePhases,
                    time, frequency: 0.22, amplitude: 16, baseHeight: 162,
                    thickness: 8, w: w, h: h);
            }

            // 3. Update Embers & Volumetric Lighting
            //
            // This block was accidentally dropped during a previous edit and
            // is the sole place where embers are moved and faded. Without it,
            // every ember sits permanently at its spawn point (the bottom of
            // the frame, behind the front mist) and never rises, which reads
            // as a scene with no sparks at all.
            //
            // Each ember is processed bottom-to-top because respawning is
            // signalled by clearing its opacity and scheduling a delay; the
            // delay tick then calls RespawnEmber to reset position and size.
            for (int i = _embers.Count - 1; i >= 0; i--)
            {
                var ember = _embers[i];

                if (ember.ParentWindow == null || !ember.ParentWindow.IsLoaded)
                {
                    _embers.RemoveAt(i);
                    continue;
                }

                if (ember.RespawnDelayTicks > 0)
                {
                    ember.RespawnDelayTicks--;
                    if (ember.RespawnDelayTicks <= 0) RespawnEmber(ember, true);
                    continue;
                }

                // Float upwards and sway horizontally. Vertical speed is
                // ember.Speed px/tick (0.28–0.83 px/tick at 30 FPS = 8–25
                // px/s), which crosses a 130-px frame in 5–15 seconds. The
                // horizontal sway uses a sine at DriftSpeed with DriftOffset
                // as the half-width of the sweep; the two are independent
                // per ember so no two sparks trace the same path.
                ember.Y -= ember.Speed;
                double currentX = ember.BaseX + Math.Sin(time * ember.DriftSpeed) * ember.DriftOffset;

                Canvas.SetLeft(ember.Glow, currentX - (ember.GlowSize / 2));
                Canvas.SetTop(ember.Glow, ember.Y - (ember.GlowSize / 2));
                Canvas.SetLeft(ember.Core, currentX - (ember.Size / 2));
                Canvas.SetTop(ember.Core, ember.Y - (ember.Size / 2));

                // Volumetric fade maths. progress is 0.0 when the ember is
                // resting at the bottom of the frame and 1.0 when it reaches
                // the top. It drives both the glow and the core opacity, in
                // opposite phases: the glow is brightest deep in the fog,
                // the core brightest in open air.
                double startY = ember.ParentWindow.ActualHeight - 10;
                double progress = 1.0 - (ember.Y / startY);

                // THE GLOW (the halo behind the core, illuminating the fog).
                //
                // Extends to progress 0.85 instead of 0.6. The original 0.6
                // cut-off was written when the large Glow ellipse carried the
                // effect; now that the Core is the primary visual, the Glow's
                // role is atmospheric fill and it should accompany the spark
                // for most of its rise so the mist visibly brightens behind
                // it.
                if (progress < 0.85)
                {
                    ember.Glow.Opacity = Math.Max(0, (1.0 - (progress / 0.85))) * ember.MaxOpacity * 0.9;
                }
                else
                {
                    ember.Glow.Opacity = 0;
                }

                // THE CORE (the crisp spark).
                //
                // The full-brightness plateau is widened to 0.12–0.85 so the
                // spark is visible for almost its entire rise. The quick
                // fade-in still reads as "emerging from the fog" and the
                // quick fade-out at the top as "burning out", but the middle
                // no longer hides the brightest moment of the spark's life.
                if (progress < 0.12)
                {
                    ember.Core.Opacity = (progress / 0.12) * ember.MaxOpacity * 0.75;
                }
                else if (progress > 0.85)
                {
                    ember.Core.Opacity = Math.Max(0, (1.0 - ((progress - 0.85) / 0.15)) * ember.MaxOpacity);
                }
                else
                {
                    ember.Core.Opacity = ember.MaxOpacity;
                }

                // Recycle: either the ember floated out of the top of the
                // frame, or its core has completely faded at the ceiling.
                // RespawnDelayTicks is scheduled rather than respawning
                // immediately, so sparks do not appear on a metronome.
                if (ember.Y < -20 || (ember.Core.Opacity <= 0 && progress > 0.5))
                {
                    ember.Core.Opacity = 0;
                    ember.Glow.Opacity = 0;
                    ember.RespawnDelayTicks = _random.Next(90, 250);
                }
            }
        }

        /// <summary>
        /// Builds a thin polygon whose top and bottom edges both follow a
        /// sine wave, producing a band of the given thickness that hovers
        /// at (h - baseHeight) in frame coordinates. The wave uses a
        /// compound sine — a primary frequency plus a secondary at 1.73×
        /// with 0.35 amplitude — so the ridge reads as a natural crest
        /// rather than as an obvious single-frequency wave. The 1.73 ratio
        /// is an irrational approximation, meaning the compound wave never
        /// perfectly repeats, which prevents the ridge from developing a
        /// visible metronome over long uptimes.
        ///
        /// Phases are taken from the parent mist's phase array, sampled at
        /// the ridge's own segment count. This is what aligns the ridge's
        /// peaks with the parent mist's peaks: a peak in the parent at
        /// segment index k corresponds to a peak in the ridge at segment
        /// index k * ridgeSegments / parentSegments. Because the ridge has
        /// more segments than the parent, this sampling produces a finer
        /// crest that still tracks the parent's contour — the visual effect
        /// is "the same body of fog, with a defined upper edge".
        /// </summary>
        private static void UpdateRidge(
            Polygon ridge,
            int parentSegments,
            double[] parentPhases,
            double time,
            double frequency,
            double amplitude,
            double baseHeight,
            double thickness,
            double w,
            double h)
        {
            if (ridge == null) return;

            int segments = Math.Max(2, parentSegments * 3 / 2);
            var points = new PointCollection();

            // Top edge, left to right.
            for (int i = 0; i < segments; i++)
            {
                double wave = RidgeWave(time, frequency, amplitude, i, segments, parentSegments, parentPhases);
                points.Add(new Point(i * (w / (segments - 1)), h - baseHeight - wave - thickness));
            }

            // Bottom edge, right to left (closes the polygon).
            for (int i = segments - 1; i >= 0; i--)
            {
                double wave = RidgeWave(time, frequency, amplitude, i, segments, parentSegments, parentPhases);
                points.Add(new Point(i * (w / (segments - 1)), h - baseHeight - wave));
            }

            ridge.Points = points;
        }

        /// <summary>
        /// Evaluates the ridge wave at segment i. The phase is looked up
        /// from the parent mist's phase array using proportional indexing,
        /// which keeps the ridge's peaks aligned with the parent's even
        /// though the two have different segment counts.
        /// </summary>
        private static double RidgeWave(
            double time,
            double frequency,
            double amplitude,
            int i,
            int segments,
            int parentSegments,
            double[] parentPhases)
        {
            double ratio = (double)i / (segments - 1);
            int parentIndex = (int)Math.Round(ratio * (parentSegments - 1));
            if (parentIndex < 0) parentIndex = 0;
            if (parentIndex >= parentPhases.Length) parentIndex = parentPhases.Length - 1;
            double phase = parentPhases[parentIndex];

            return Math.Sin(time * frequency + phase) * amplitude
                 + Math.Sin(time * frequency * 1.73 + phase * 1.31) * amplitude * 0.35;
        }

        /// <summary>
        /// Walks a pumpkin's visual tree to locate the face-fill brush so the
        /// hue-rotation timer can retint the eyes, nose and mouth along with
        /// the surrounding glow. The face canvas is the only child of the
        /// pumpkin Canvas that carries a DropShadowEffect; the face fill
        /// brush is the only SolidColorBrush shared by its eye polygons.
        /// Returned by reference so the caller mutates the same brush the
        /// face polygons are already using.
        /// </summary>
        private static SolidColorBrush FindFaceFill(Canvas pumpkin)
        {
            foreach (var child in pumpkin.Children)
            {
                if (child is Canvas face && face.Effect is DropShadowEffect)
                {
                    foreach (var shape in face.Children)
                    {
                        if (shape is Shape s && s.Fill is SolidColorBrush b)
                            return b;
                    }
                }
            }
            return null;
        }

        private static void InjectHalloweenCanvas(NonActivatingWindow frame)
        {
            try
            {
                var mainBorder = frame.Content as Border;
                if (mainBorder == null) return;

                if (mainBorder.Child is Grid originalGrid && originalGrid.Name == "HalloweenGrid") return;

                var originalInner = mainBorder.Child;
                mainBorder.Child = null;

                var rootGrid = new Grid { Name = "HalloweenGrid" };

                // -----------------------------------------------------------------
                // LAYER 0: Night sky gradient — on its own canvas BEHIND the
                // frame's own content.
                //
                // Placing this on a separate canvas beneath originalInner means
                // the gradient tints the frame's background colour toward a
                // deep night hue, but never darkens the icons, the title text
                // or the filter bar. The rectangle is resized every tick to
                // track frame resizing.
                // -----------------------------------------------------------------
                var skyCanvas = new Canvas { IsHitTestVisible = false, ClipToBounds = true };
                var skyGradient = new Rectangle
                {
                    IsHitTestVisible = false,
                    Width = Math.Max(100, frame.ActualWidth),
                    Height = Math.Max(60, frame.ActualHeight * 0.40),
                    Fill = new LinearGradientBrush
                    {
                        StartPoint = new Point(0, 0),
                        EndPoint = new Point(0, 1),
                        GradientStops = new GradientStopCollection
                        {
                            // Deep purple-indigo at the very top — the colour
                            // the sky reads as just after sunset, before stars
                            // win. Fades to nothing by the middle of the frame.
                            new GradientStop(Color.FromArgb(170, 22, 14, 48), 0.0),
                            new GradientStop(Color.FromArgb(90, 22, 14, 48), 0.55),
                            new GradientStop(Color.FromArgb(0, 22, 14, 48), 1.0)
                        }
                    }
                };
                skyCanvas.Children.Add(skyGradient);

                // -----------------------------------------------------------------
                // The moon: a soft disc with a wide, cool halo and two thin
                // cloud wisps crossing its face.
                //
                // The moon lives on the sky canvas, which sits *behind* the
                // frame's own content. That placement is deliberate: the
                // frame's semi-transparent coloured background tints the moon
                // toward the frame's hue, exactly as a real moon seen through
                // a tinted window would be tinted. A moon drawn on top of the
                // frame's content instead reads as a sticker pasted to the
                // front of the scene, which is not the effect we want.
                //
                // The canvas is 100x100 with the disc centred at (50,50).
                // Positioning is done in the physics loop; the initial
                // placement here is only a best-guess so nothing flashes at
                // the origin on the first frame before the tick fires.
                // -----------------------------------------------------------------
                var moonCanvas = new Canvas
                {
                    Width = 100,
                    Height = 100,
                    IsHitTestVisible = false
                };

                // Outer halo — a wide, cool, very soft radial glow. This is
                // what sells the "misty" read: a moon with a hard edge looks
                // crisp and distant, whereas a moon with a generous halo
                // looks like it is being seen through a couple of kilometres
                // of haze.
                var moonHalo = new Ellipse
                {
                    Width = 100,
                    Height = 100,
                    Fill = new RadialGradientBrush
                    {
                        GradientStops = new GradientStopCollection
                        {
                            new GradientStop(Color.FromArgb(70, 200, 195, 235), 0.0),
                            new GradientStop(Color.FromArgb(35, 200, 195, 235), 0.4),
                            new GradientStop(Color.FromArgb(0, 200, 195, 235), 1.0)
                        }
                    }
                };
                moonCanvas.Children.Add(moonHalo);

                // The disc itself. Warm white in the centre, cooling toward
                // the edge, with a soft fade instead of a hard rim. The
                // gradient origin is offset slightly up-left of centre so
                // the moon reads as lit from one side rather than as a
                // flat coin.
                // Atmospheric haze band behind the moon. This is what turns
                // the moon from "disc floating in sky" into "moon seen
                // through several kilometres of atmosphere": a very wide,
                // very soft ellipse of cool translucent white, roughly 4×
                // the disc's diameter, sitting behind it. Because the sky
                // canvas lives *behind* the frame's own content, this haze
                // also picks up the frame's tint — so the moon's glow
                // varies in warmth with the frame colour, which is the
                // small per-frame difference that makes a set of frames
                // read as "the same sky, viewed through different windows".
                var moonHaze = new Ellipse
                {
                    Width = 140,
                    Height = 140,
                    Fill = new RadialGradientBrush
                    {
                        GradientStops = new GradientStopCollection
                        {
                            new GradientStop(Color.FromArgb(60, 210, 205, 240), 0.0),
                            new GradientStop(Color.FromArgb(35, 210, 205, 240), 0.45),
                            new GradientStop(Color.FromArgb(12, 210, 205, 240), 0.75),
                            new GradientStop(Color.FromArgb(0, 210, 205, 240), 1.0)
                        }
                    }
                };
                Canvas.SetLeft(moonHaze, -20);
                Canvas.SetTop(moonHaze, -20);
                moonCanvas.Children.Add(moonHaze);

                var moonDisc = new Ellipse
                {
                    Width = 32,
                    Height = 32,
                    Fill = new RadialGradientBrush
                    {
                        GradientOrigin = new Point(0.35, 0.35),
                        Center = new Point(0.5, 0.5),
                        RadiusX = 0.7,
                        RadiusY = 0.7,
                        GradientStops = new GradientStopCollection
                        {
                            new GradientStop(Color.FromArgb(240, 255, 250, 235), 0.0),
                            new GradientStop(Color.FromArgb(210, 240, 235, 215), 0.55),
                            new GradientStop(Color.FromArgb(90, 220, 215, 200), 0.82),
                            new GradientStop(Color.FromArgb(0, 220, 215, 200), 1.0)
                        }
                    }
                };
                Canvas.SetLeft(moonDisc, 34);
                Canvas.SetTop(moonDisc, 34);
                moonCanvas.Children.Add(moonDisc);

                // Two thin cloud wisps crossing the moon at different heights
                // and with different widths. They use the sky gradient's own
                // purple-black so they blend with the sky, and carry a heavy
                // blur so they read as fog rather than as rectangles.
                //
                // The bottom wisp crosses the lower third of the disc —
                // enough that the moon looks partially occluded, not just
                // dimmed. The top wisp is shorter and thinner and touches
                // the upper-left of the disc, giving the sky a hint of motion
                // without swamping the moon's face.
                var moonCloudDrift = new TranslateTransform(0, 0);

                var bottomCloud = new Rectangle
                {
                    Width = 96,
                    Height = 16,
                    RadiusX = 8,
                    RadiusY = 8,
                    Fill = new SolidColorBrush(Color.FromArgb(180, 18, 10, 40)),
                    Effect = new BlurEffect { Radius = 6 },
                    RenderTransform = moonCloudDrift
                };
                Canvas.SetLeft(bottomCloud, 2);
                Canvas.SetTop(bottomCloud, 52);
                moonCanvas.Children.Add(bottomCloud);

                var topWisp = new Rectangle
                {
                    Width = 64,
                    Height = 9,
                    RadiusX = 5,
                    RadiusY = 5,
                    Fill = new SolidColorBrush(Color.FromArgb(150, 15, 8, 35)),
                    Effect = new BlurEffect { Radius = 5 }
                };
                Canvas.SetLeft(topWisp, 22);
                Canvas.SetTop(topWisp, 28);
                moonCanvas.Children.Add(topWisp);

                skyCanvas.Children.Add(moonCanvas);
                rootGrid.Children.Add(skyCanvas);

                // Frame's own content goes in the middle of the z-stack.
                if (originalInner != null) rootGrid.Children.Add(originalInner);

                // Main effect canvas sits on top so mist and embers paint over
                // the frame's content, exactly as before.
                var canvas = new Canvas { IsHitTestVisible = false, ClipToBounds = true };
                rootGrid.Children.Add(canvas);

                // LAYER 1: Back Mist (Deep Purple depth)
                var backMist = new Rectangle
                {
                    Height = 120,
                    IsHitTestVisible = false,
                    Fill = new LinearGradientBrush
                    {
                        StartPoint = new Point(0, 0),
                        EndPoint = new Point(0, 1),
                        GradientStops = new GradientStopCollection
                        {
                            new GradientStop(Color.FromArgb(0, 20, 0, 30), 0.0),
                            new GradientStop(Color.FromArgb(120, 25, 5, 40), 0.6),
                            new GradientStop(Color.FromArgb(200, 10, 0, 20), 1.0)
                        }
                    },
                    Effect = new BlurEffect { Radius = 30 }
                };
                canvas.Children.Add(backMist);

                // -----------------------------------------------------------------
                // LAYER 1.5: High ridge.
                //
                // The upper-most atmospheric band, sitting roughly 150px above
                // the frame's bottom edge. Very thin (8px), very translucent
                // (top alpha 60 / bottom alpha 90), and blurred just enough to
                // read as suspended vapour. Its role is compositional: it
                // gives the mist region vertical extent and prevents the
                // upper half of the frame from reading as empty sky.
                //
                // It has no parent mist to mirror, so it carries its own
                // phase array (populated in the injection block at the
                // bottom of this method). Its fill is cooler than the mid
                // mist's, pulling the sky end of the palette toward purple
                // so the gradient from back mist to front mist traces a
                // hue arc from cold/atmospheric to warm/earthen.
                // -----------------------------------------------------------------
                var highRidge = new Polygon
                {
                    Fill = new LinearGradientBrush
                    {
                        StartPoint = new Point(0, 0),
                        EndPoint = new Point(0, 1),
                        GradientStops = new GradientStopCollection
                        {
                            new GradientStop(Color.FromArgb(60, 60, 45, 85), 0.0),
                            new GradientStop(Color.FromArgb(90, 45, 32, 68), 1.0)
                        }
                    },
                    IsHitTestVisible = false,
                    Effect = new BlurEffect { Radius = 14 }
                };
                canvas.Children.Add(highRidge);

                // LAYER 2: Mid Mist (olive-purple, slow horizontal drift).
                //
                // Z-order note — this must sit BELOW the ember glows.
                //
                // The glows are the "inverted gravity" atmospheric core of the
                // effect: bright spots of light scattered inside the fog that
                // ride up with each spark. Their visible lifetime spans
                // roughly the bottom 60% of the frame, which is exactly the
                // region the mid mist occupies. If the mid mist is drawn on
                // top of the glows (as it was in the first revision of this
                // layer), a 120–170 alpha fog washes them out almost entirely
                // and the scene reads as "just sparks floating in nothing",
                // losing the sense that the fog itself is glowing from within.
                //
                // Placing the mid mist below the glows restores the original
                // relationship: the glow sits in front of the mid mist and
                // behind only the front mist — one fog layer in front of the
                // glow, one behind it, exactly as the pre-mid-mist layout had.
                //
                // The horizontal drift is applied via RenderTransform.X,
                // driven by the physics loop. The polygon spans ~80px past
                // each canvas edge so the drift never opens a visible gap.
                var midMist = new Polygon
                {
                    Fill = new LinearGradientBrush
                    {
                        StartPoint = new Point(0, 0),
                        EndPoint = new Point(0, 1),
                        GradientStops = new GradientStopCollection
                        {
                            new GradientStop(Color.FromArgb(0, 45, 30, 55), 0.0),
                            new GradientStop(Color.FromArgb(120, 38, 26, 52), 0.55),
                            new GradientStop(Color.FromArgb(170, 30, 20, 45), 1.0)
                        }
                    },
                    IsHitTestVisible = false,
                    Effect = new BlurEffect { Radius = 26 },
                    RenderTransform = new TranslateTransform(0, 0)
                };
                canvas.Children.Add(midMist);

                // -----------------------------------------------------------------
                // LAYER 2.7: Mid ridge crest.
                //
                // A thin (10px) band whose top edge mirrors the mid mist's
                // contour, scaled to 85% amplitude and lifted 22px so it
                // sits just above the parent's peaks. The 20 segments (vs
                // the mid mist's 14) give it finer sub-detail — a subtle
                // "echo" that reads as mist layer boundaries within the
                // same fog body.
                //
                // Colour is slightly warmer than the mid mist so it reads
                // as a distinct layer where the two overlap. Blur is
                // smaller than the parent's so the ridge reads sharper —
                // this is what creates the "defined contour" impression
                // that turns a soft mist blob into a cinematic ridge line.
                // -----------------------------------------------------------------
                var midRidge = new Polygon
                {
                    Fill = new LinearGradientBrush
                    {
                        StartPoint = new Point(0, 0),
                        EndPoint = new Point(0, 1),
                        GradientStops = new GradientStopCollection
                        {
                            new GradientStop(Color.FromArgb(70, 55, 38, 60), 0.0),
                            new GradientStop(Color.FromArgb(130, 42, 28, 48), 1.0)
                        }
                    },
                    IsHitTestVisible = false,
                    Effect = new BlurEffect { Radius = 16 }
                };
                canvas.Children.Add(midRidge);

                // LAYER 3: Ember light scatters (in front of the mid mist,
                // behind the front mist — same z-relationship the glows had
                // before the mid mist was introduced).
                //
                // Six embers per frame. With the enlarged cores each spark
                // is individually more visible; raising the count too keeps
                // the frame from feeling sparse. Do not raise past eight:
                // past that the sparks merge into a field rather than
                // reading as discrete rising particles, which loses the
                // "inverted gravity" impression the effect is going for.
                for (int i = 0; i < 6; i++)
                {
                    var glow = new Ellipse
                    {
                        Width = 80,
                        Height = 80,
                        Fill = new RadialGradientBrush(Colors.DarkOrange, Colors.Transparent),
                        IsHitTestVisible = false,
                        Opacity = 0,
                        Effect = new BlurEffect { Radius = 20 }
                    };
                    canvas.Children.Add(glow);

                    // The core is deliberately drawn on top of every mist
                    // layer, so it is the one part of the ember that can
                    // never be washed out by the fog. We therefore give it
                    // its own warm halo, supplied by a DropShadowEffect
                    // with ShadowDepth = 0 (which renders as a symmetric
                    // glow around the source). This halo replaces the
                    // original BlurEffect: a BlurEffect softens the source
                    // but adds nothing around it, whereas the shadow
                    // produces a warm bloom that reads at small sizes.
                    var core = new Ellipse
                    {
                        Fill = new SolidColorBrush(Color.FromRgb(255, 200, 50)),
                        IsHitTestVisible = false,
                        Opacity = 0,
                        Effect = new DropShadowEffect
                        {
                            Color = Color.FromRgb(255, 190, 90),
                            BlurRadius = 8,
                            ShadowDepth = 0,
                            Opacity = 0.9
                        }
                    };
                    // Core is added to the canvas AFTER Front Mist, below.

                    var ember = new Ember { Core = core, Glow = glow, ParentWindow = frame };
                    ember.RespawnDelayTicks = _random.Next(10, 200);
                    _embers.Add(ember);
                }

                // LAYER 3: The Pumpkins (sitting half-buried in the fog).
                // Position is set by the physics loop; initial placement here
                // is a best-guess baseline so nothing flashes at origin.
                var (bigPumpkin, bigFaceGlow, bigFaceFill) = CreateProceduralPumpkin(1.1);
                var (smallPumpkin, smallFaceGlow, smallFaceFill) = CreateProceduralPumpkin(0.7);
                Canvas.SetLeft(bigPumpkin, Math.Max(0, frame.ActualWidth - 85));
                Canvas.SetTop(bigPumpkin, Math.Max(0, frame.ActualHeight - 36));
                Canvas.SetLeft(smallPumpkin, Math.Max(0, frame.ActualWidth - 40));
                Canvas.SetTop(smallPumpkin, Math.Max(0, frame.ActualHeight - 24));
                canvas.Children.Add(bigPumpkin);
                canvas.Children.Add(smallPumpkin);

                // LAYER 4: Front Mist (murky green, animated rolling peaks).
                var frontMist = new Polygon
                {
                    Fill = new LinearGradientBrush
                    {
                        StartPoint = new Point(0, 0),
                        EndPoint = new Point(0, 1),
                        GradientStops = new GradientStopCollection
                        {
                            new GradientStop(Color.FromArgb(0, 10, 25, 10), 0.0),
                            new GradientStop(Color.FromArgb(180, 5, 15, 5), 1.0)
                        }
                    },
                    IsHitTestVisible = false,
                    Effect = new BlurEffect { Radius = 25 }
                };
                canvas.Children.Add(frontMist);

                // -----------------------------------------------------------------
                // LAYER 4.5: Front ridge crest.
                //
                // The most prominent of the three ridges. The front mist is
                // the layer closest to the viewer and its contour is what
                // the eye reads as "the fog line", so this ridge carries
                // slightly more alpha (top 85 / bottom 145) than the two
                // above it. It follows the front mist's contour at 90%
                // amplitude, lifted 20px, with 18 segments (vs the parent's
                // 12) for a detail layer that reads as fine rolling crests.
                //
                // Warmest of the three ridges, pulling toward the earthy
                // green-brown of the front mist itself so the whole stack
                // traces a temperature gradient from cool (high) to warm
                // (low) — the way real fog over a warm body of water does.
                // -----------------------------------------------------------------
                var frontRidge = new Polygon
                {
                    Fill = new LinearGradientBrush
                    {
                        StartPoint = new Point(0, 0),
                        EndPoint = new Point(0, 1),
                        GradientStops = new GradientStopCollection
                        {
                            new GradientStop(Color.FromArgb(85, 22, 34, 22), 0.0),
                            new GradientStop(Color.FromArgb(145, 12, 22, 12), 1.0)
                        }
                    },
                    IsHitTestVisible = false,
                    Effect = new BlurEffect { Radius = 18 }
                };
                canvas.Children.Add(frontRidge);

                // LAYER 5: Ember cores (crisp sparks emerging from the fog).
                foreach (var ember in _embers.Where(e => e.ParentWindow == frame))
                {
                    canvas.Children.Add(ember.Core);
                }

                mainBorder.Child = rootGrid;

                // Per-frame random phases and per-frame random mist segment
                // counts. Together these ensure that no two frames on the
                // desktop visually rhyme, so the set reads as "windows into the
                // same night" rather than "the same animation tiled".
                int mistSegments = 12;
                int midMistSegments = 14;
                var phases = new double[mistSegments];
                for (int i = 0; i < mistSegments; i++) phases[i] = _random.NextDouble() * Math.PI * 2;
                var midPhases = new double[midMistSegments];
                for (int i = 0; i < midMistSegments; i++) midPhases[i] = _random.NextDouble() * Math.PI * 2;

                // The high ridge has no parent mist to inherit phases from,
                // so it carries its own. Generating them from the same
                // _random instance (which is not reseeded per frame) keeps
                // every frame's phase set independent, which is what
                // prevents two frames on the desktop from having visibly
                // matching mist patterns.
                int highRidgeSegments = 22;
                var highRidgePhases = new double[highRidgeSegments];
                for (int i = 0; i < highRidgeSegments; i++) highRidgePhases[i] = _random.NextDouble() * Math.PI * 2;

                double now = DateTime.Now.TimeOfDay.TotalSeconds;

                _halloweenContexts[frame] = new HalloweenContext
                {
                    Canvas = canvas,
                    SkyGradient = skyGradient,
                    MoonCanvas = moonCanvas,
                    MoonCloudDrift = moonCloudDrift,
                    BackMist = backMist,
                    MidMist = midMist,
                    FrontMist = frontMist,
                    BigPumpkin = bigPumpkin,
                    SmallPumpkin = smallPumpkin,
                    BigPumpkinFaceGlow = bigFaceGlow,
                    SmallPumpkinFaceGlow = smallFaceGlow,
                    BigPumpkinFlickerPhase = _random.NextDouble() * Math.PI * 2,
                    SmallPumpkinFlickerPhase = _random.NextDouble() * Math.PI * 2,
                    BigPumpkinNextColorChange = now + _random.NextDouble() * 12 + 8,
                    SmallPumpkinNextColorChange = now + _random.NextDouble() * 12 + 8,
                    BigPumpkinColorIndex = 0,
                    SmallPumpkinColorIndex = 0,
                    Segments = mistSegments,
                    MidMistSegments = midMistSegments,
                    MistPhases = phases,
                    MidMistPhases = midPhases,
                    FrontRidge = frontRidge,
                    MidRidge = midRidge,
                    HighRidge = highRidge,
                    HighRidgePhases = highRidgePhases
                };
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.UI, $"InterCore: Failed to inject Halloween theme. {ex.Message}");
            }
        }

        private static void RespawnEmber(Ember ember, bool placeAtBottom)
        {
            // PARALLAX DEPTH ENGINE: 0.0 is background (far), 1.0 is foreground (close)
            double depth = _random.NextDouble();

            // Scale all properties perfectly based on the Z-Depth.
            //
            // These sizes are deliberately larger than the original effect's.
            // That version leaned on the large *Glow* ellipse to sell each
            // spark; the Glow sits behind the front mist, which carries
            // alpha ~180 near the bottom of the frame. With the mid mist
            // now also present, the Glow's effective on-screen brightness is
            // roughly one third of its nominal opacity, so it can no longer
            // be the primary visual. The Core, which is drawn on top of
            // every mist layer, must carry the effect alone — and for that
            // it needs to be visible at a glance, not just present.
            //
            // The rise speed is also increased: at the original 0.15-0.5
            // px/tick (30 FPS), a spark took 13-44 seconds to cross the
            // frame, which reads as drift rather than fire.
            ember.Size = 2.5 + (depth * 4.0);         // Core size: 2.5px (far) to 6.5px (close)
            ember.GlowSize = 40.0 + (depth * 90.0);   // Glow volume: 40px (far) to 130px (close)
            ember.MaxOpacity = 0.45 + (depth * 0.55); // Brightness: 0.45 (dim/far) to 1.0 (bright/close)
            ember.Speed = 0.28 + (depth * 0.55);      // Rise speed: 0.28 (slow/far) to 0.83 (fast/close)
            ember.DriftOffset = 4.0 + (depth * 20.0); // Sway width: 4px (narrow/far) to 24px (wide/close)
            ember.DriftSpeed = 0.5 + (depth * 1.3);   // Sway speed: sluggish (far) to active (close)

            // Apply calculated sizes
            ember.Core.Width = ember.Size;
            ember.Core.Height = ember.Size;
            ember.Glow.Width = ember.GlowSize;
            ember.Glow.Height = ember.GlowSize;

            double width = Math.Max(100, ember.ParentWindow.ActualWidth);
            ember.BaseX = _random.NextDouble() * width;

            if (placeAtBottom)
            {
                ember.Y = ember.ParentWindow.ActualHeight - 10;
            }

            // Instantly move both pieces using dynamic center offsets
            Canvas.SetLeft(ember.Glow, ember.BaseX - (ember.GlowSize / 2));
            Canvas.SetTop(ember.Glow, ember.Y - (ember.GlowSize / 2));
            Canvas.SetLeft(ember.Core, ember.BaseX - (ember.Size / 2));
            Canvas.SetTop(ember.Core, ember.Y - (ember.Size / 2));

            // ---------------------------------------------------------------
            // Ember hue by depth (atmospheric perspective)
            //
            // Distant embers read cool and pale — their light has been
            // scattered by more of the fog before reaching the eye. Close
            // embers read warm and saturated. This reinforces the parallax
            // established by the size / opacity / speed calculations above,
            // and it makes overlapping embers distinguishable from one
            // another instead of merging into a single bright blob.
            //
            // The core is set to the pure hue; the glow fades to fully
            // transparent in the same hue so the two elements never read as
            // two different objects. The glow brush is frozen because nothing
            // animates it (only the Ellipse's Opacity is animated).
            // ---------------------------------------------------------------
            Color emberColor;
            if (depth < 0.25) emberColor = Color.FromRgb(255, 240, 210); // Pale candle — far
            else if (depth < 0.55) emberColor = Color.FromRgb(255, 210, 130); // Light gold
            else if (depth < 0.80) emberColor = Color.FromRgb(255, 170, 80);  // Orange
            else emberColor = Color.FromRgb(255, 130, 45);  // Deep orange-red — near

            ember.Core.Fill = new SolidColorBrush(emberColor);

            var glowBrush = new RadialGradientBrush(
                Color.FromArgb(255, emberColor.R, emberColor.G, emberColor.B),
                Color.FromArgb(0, emberColor.R, emberColor.G, emberColor.B));
            glowBrush.Freeze();
            ember.Glow.Fill = glowBrush;
        }

        // Returns the pumpkin canvas plus the face-glow effect and the face
        // fill brush. The caller stores the glow reference on the context so
        // the physics loop can flicker it (and occasionally swap its hue)
        // without walking the visual tree each tick. The fill brush is
        // returned so hue swaps tint both the cut-outs and their surrounding
        // bleed together, keeping the "candle behind the face" illusion
        // intact — changing only the glow would look like two lights.
        private static (Canvas Visual, DropShadowEffect FaceGlow, SolidColorBrush FaceFill) CreateProceduralPumpkin(double scale)
        {
            var pumpkinBox = new Canvas { Width = 40 * scale, Height = 35 * scale, IsHitTestVisible = false };

            // 1. The Stem
            var stem = new Polygon
            {
                Fill = new SolidColorBrush(Color.FromRgb(60, 80, 30)), // Dark murky green
                Points = new PointCollection
                {
                    new Point(17 * scale, 12 * scale),
                    new Point(23 * scale, 12 * scale),
                    new Point(26 * scale, 2 * scale),
                    new Point(19 * scale, 0)
                }
            };
            pumpkinBox.Children.Add(stem);

            // 2. The Ribbed Body
            var darkOrange = new SolidColorBrush(Color.FromRgb(200, 70, 0));
            var brightOrange = new SolidColorBrush(Color.FromRgb(255, 100, 0));

            var leftLobe = new Ellipse { Width = 16 * scale, Height = 22 * scale, Fill = darkOrange };
            Canvas.SetLeft(leftLobe, 2 * scale); Canvas.SetTop(leftLobe, 11 * scale);
            pumpkinBox.Children.Add(leftLobe);

            var rightLobe = new Ellipse { Width = 16 * scale, Height = 22 * scale, Fill = darkOrange };
            Canvas.SetLeft(rightLobe, 22 * scale); Canvas.SetTop(rightLobe, 11 * scale);
            pumpkinBox.Children.Add(rightLobe);

            var centerLobe = new System.Windows.Shapes.Ellipse { Width = 20 * scale, Height = 25 * scale, Fill = brightOrange };
            Canvas.SetLeft(centerLobe, 10 * scale); Canvas.SetTop(centerLobe, 10 * scale);
            pumpkinBox.Children.Add(centerLobe);

            // 3. The Jack-o'-lantern Face (Glowing cutouts)
            var faceCanvas = new Canvas();
            // Start from the palette's default hue. The physics loop may swap
            // this brush's Color at runtime when the hue-rotation timer fires.
            var glowBrush = new SolidColorBrush(PumpkinFacePalette[0].Color);

            // Left Eye
            var leftEye = new System.Windows.Shapes.Polygon { Fill = glowBrush, Points = new PointCollection { new Point(13 * scale, 18 * scale), new Point(17 * scale, 18 * scale), new Point(15 * scale, 14 * scale) } };
            // Right Eye
            var rightEye = new System.Windows.Shapes.Polygon { Fill = glowBrush, Points = new PointCollection { new Point(23 * scale, 18 * scale), new Point(27 * scale, 18 * scale), new Point(25 * scale, 14 * scale) } };
            // Nose
            var nose = new System.Windows.Shapes.Polygon { Fill = glowBrush, Points = new PointCollection { new Point(19 * scale, 21 * scale), new Point(21 * scale, 21 * scale), new Point(20 * scale, 19 * scale) } };
            // Jagged Mouth
            var mouth = new System.Windows.Shapes.Polygon
            {
                Fill = glowBrush,
                Points = new PointCollection
                {
                    new Point(12 * scale, 24 * scale), new Point(15 * scale, 27 * scale), new Point(17 * scale, 25 * scale),
                    new Point(20 * scale, 28 * scale), new Point(23 * scale, 25 * scale), new Point(25 * scale, 27 * scale),
                    new Point(28 * scale, 24 * scale), new Point(25 * scale, 30 * scale), new Point(20 * scale, 31 * scale),
                    new Point(15 * scale, 30 * scale)
                }
            };

            faceCanvas.Children.Add(leftEye);
            faceCanvas.Children.Add(rightEye);
            faceCanvas.Children.Add(nose);
            faceCanvas.Children.Add(mouth);

            // Intense inner fire glow on the face. Kept as a named local so the
            // caller can flicker its Opacity and, occasionally, retarget its
            // Color at runtime.
            var faceGlow = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = PumpkinFacePalette[0].Color,
                BlurRadius = 8 * scale,
                ShadowDepth = 0,
                Opacity = 1.0
            };
            faceCanvas.Effect = faceGlow;

            pumpkinBox.Children.Add(faceCanvas);

            // 4. The Ambient Backlight Glow. Left as a warm orange halo so the
            // pumpkin still reads as "warm" even when the face briefly swings
            // to one of the rarer hues.
            pumpkinBox.Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Color.FromRgb(255, 80, 0),
                BlurRadius = 15 * scale,
                ShadowDepth = 0,
                Opacity = 0.7
            };

            return (pumpkinBox, faceGlow, glowBrush);
        }

        #endregion
    }
}