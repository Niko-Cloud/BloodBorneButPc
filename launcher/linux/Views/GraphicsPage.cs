using System.IO;
using Avalonia;
using Avalonia.Controls;
using BloodborneLauncher.Core;
using FluentIcons.Common;

namespace BloodborneLauncher.Views
{
    /// <summary>Upscaling and the game's own effects (patches applied at start).</summary>
    public sealed class GraphicsPage : LauncherPage
    {
        private readonly TextBlock _upscalerHint, _dlssHint;
        private readonly SettingRow _presetCard, _sharpenCard, _sharpnessCard;
        private readonly UiButton _dlssDownload;

        public GraphicsPage() : base("Graphics",
            "Stored in bbport.ini; the upscaler can also be changed in game (Insert or L3 + R3).")
        {
            Add(Card(Symbol.Gauge, "Best settings for this PC", Hardware.Detect().Summary,
                SmallButton("Recommend…", Symbol.Sparkle, (_, _) => MainWindow.Instance!.OfferBestSettings(firstRun: false)), out _));

            Group("Upscaler");
            // FSR 4.1.1 runs through fsr4vk (in the Windows package) or an fsr4_411 set built from the
            // player's own AMD DLL: offered when either is there. Without both the setting becomes
            // FSR 4, the model it would fall back to (#22).
            var fsr411 = Fsr4VkFound() || Directory.Exists(Fsr411Dir());
            if (!fsr411 && Ini.Get("upscaler") == "fsr411") Ini.Set("upscaler", "fsr4");
            IniCombo(Symbol.Sparkle, "Upscaler", null,
                fsr411 ? Choices.Upscalers : Choices.Upscalers.Where(c => c.Value != "fsr411").ToArray(), "upscaler", changed: UpdateUpscaler);
            _upscalerHint = (TextBlock)((SettingRow)Body.Children[^1]).Tag!;

            IniCombo(Symbol.ZoomIn, "Quality preset", "How far below the output resolution the game renders",
                Choices.Presets, "preset", changed: UpdateUpscaler);
            _presetCard = (SettingRow)Body.Children[^1];

            IniToggle(Symbol.Eyedropper, "Sharpening (RCAS)", "Works with every option above, TAA included: turn it up if the picture looks soft", "sharpen");
            _sharpenCard = (SettingRow)Body.Children[^1];
            SliderRow(Symbol.SlideSize, "Sharpening strength", "Up to 1: the upscaler's own sharpening. Above 1: an extra pass", 0, 2, 0.05,
                () => ParseDouble(Ini.Get("sharpness"), 0.5), v => Ini.Set("sharpness", Fmt(v)));
            _sharpnessCard = (SettingRow)Body.Children[^1];

            IniToggle(Symbol.ArrowMove, "Object motion vectors", "Less ghosting on characters; costs about 10% FPS", "object_motion");
            IniToggle(Symbol.DataUsage, "Show FPS", null, "show_fps");
            // What the counter shows (on unless turned off).
            foreach (var (icon, key, title) in new[]
                     {
                         (Symbol.TopSpeed, "show_fps_rate", "Frame rate"),
                         (Symbol.Timer, "show_fps_frame_time", "Frame time"),
                         (Symbol.ResizeLarge, "show_fps_upscaler", "Upscaler"),
                         (Symbol.DeveloperBoard, "show_fps_cpu", "CPU usage"),
                         (Symbol.Gauge, "show_fps_gpu", "GPU usage"),
                     })
            {
                // CPU and GPU use are off unless chosen (the engine's defaults, bbport_settings.h).
                var fallback = key is "show_fps_cpu" or "show_fps_gpu" ? "0" : "1";
                ToggleRow(icon, title, "Shown in the FPS counter", () => Ini.Get(key, fallback) == "1", v => Ini.Set(key, v ? "1" : "0"));
                Body.Children[^1].Margin = new Thickness(24, 0, 0, 4);
            }

            // Linux: NVIDIA's DLSS runtime from the public DLSS SDK (tools/fetch_dlss.sh in the engine).
            _dlssDownload = SmallButton("Download", Symbol.ArrowDownload, async (_, _) => await DownloadDlssAsync());
            _dlssDownload.IsVisible = false;
            Add(Card(Symbol.DeveloperBoard, "DLSS", "", _dlssDownload, out _dlssHint));

            // The engine's picture and camera controls (bbport_settings.cpp clamps the same ranges).
            Group("Picture");
            var picture = new StackPanel();
            IniToggle(Symbol.Color, "Picture tuning", "Turns the settings below on, live. Also in the game's System menu (PC Graphics, PC Effects, PC Colour) and the Insert menu", "graphics_controls");
            IniToggle(Symbol.CircleHalfFill, "Vignette", null, "graphics_vignette", picture);
            IniSlider(Symbol.DarkTheme, "Ambient occlusion strength", 0, 3, "graphics_ao_strength", picture);
            IniSlider(Symbol.WeatherSunny, "Shadow distance", 0.5, 4, "graphics_shadow_scale", picture);
            IniSlider(Symbol.Lightbulb, "Bloom", 0, 3, "graphics_bloom", picture);
            IniSlider(Symbol.Color, "Saturation", 0, 3, "graphics_saturation", picture);
            IniSlider(Symbol.BrightnessHigh, "Brightness", 0, 3, "graphics_brightness", picture);
            IniSlider(Symbol.DarkTheme, "Contrast", 0.8, 1.2, "graphics_contrast", picture, step: 0.01);
            IniSlider(Symbol.Color, "Hue", -180, 180, "graphics_hue", picture, step: 1, fallback: 0);
            IniSlider(Symbol.WeatherFog, "Fog", 0, 3, "graphics_fog", picture);
            Body.Children.Add(picture);

            Group("Camera");
            IniToggle(Symbol.Camera, "Camera tuning", "Turns the three settings below on", "camera_controls");
            IniSlider(Symbol.ZoomOut, "Field of view", 1, 1.5, "camera_fov_scale");
            IniSlider(Symbol.ArrowMaximize, "Distance", 0.5, 1.5, "camera_distance_scale");
            IniSlider(Symbol.ArrowBidirectionalUpDown, "Height", 0.5, 1.5, "camera_height_scale");

            // Engine features from 0.9.4, each with a switch, as IFreemz's "Bloodborne - Experimental".
            Group("Experimental");
            SettingToggle(Symbol.Broom, "VRAM cleanup",
                "Frees video memory held by textures the GPU drew, so long sessions don't fill up VRAM and slow down. Recommended: on",
                "exp_vram_cleanup");
            SettingToggle(Symbol.Timer, "Precise frame pacing",
                "A high-resolution timer and even spacing between frames: steadier frame times and less VRR flicker. Uses a little CPU. Off by default",
                "exp_frame_pacing");
            SettingToggle(Symbol.Storage, "Use more VRAM for textures",
                "Keeps textures in video memory up to 80% of the card instead of about 40%, so they aren't thrown out and loaded again (the cause of most stutters on 8 GB cards). Off by default while it is tested",
                "exp_vram_budget");
            SettingToggle(Symbol.TopSpeed, "Graphics threads first",
                "Runs the engine's graphics threads at a higher CPU priority than the game's other threads, so a frame isn't held up behind them. Off by default",
                "exp_thread_priority");
            SettingToggle(Symbol.Sleep, "Shorter sleep spin",
                "The game's many short sleeps busy-wait only as long as the timer needs (about 0.2 to 0.4 ms instead of 0.6 ms), leaving that CPU time to the graphics threads. Off by default",
                "exp_sleep_spin");
            SettingToggle(Symbol.FlashFlow, "Background shader compiling",
                "New shaders compile on helper threads instead of freezing the frame: much shorter hitches when you reach a new area. On by default",
                "exp_async_pipelines");
            SettingToggle(Symbol.EyeOff, "Occlusion culling",
                "The game skips objects hidden behind walls instead of drawing them all: much higher FPS in dense areas. Turn it off if lamps or glare flicker",
                "exp_occlusion");
            SettingToggle(Symbol.FastForward, "Faster draw thread",
                "Remembers texture and sampler lookups and hands the end of each frame to the draw thread: higher FPS in busy areas. New: turn it off if the game hangs or crashes",
                "exp_fast_draws");
            SettingToggle(Symbol.Shapes, "No shader freezes",
                "While a new shader compiles, the objects that need it appear a moment later instead of the game freezing. New: turn it off if something stays missing",
                "exp_compile_skip");
            SettingToggle(Symbol.Run, "Sharper motion (FSR 4)",
                "Uses FSR 4's balanced model at Performance and Ultra Performance: fewer trails behind moving characters, a little more GPU time",
                "fsr4_balanced");

            Group("Game effects");
            Body.Children.Add(Text("Patches to the game, applied when it starts.", "Faint"));
            IniCombo(Symbol.Cube, "Model detail", null, Choices.ModelLod, "model_lod");
            foreach (var effect in Choices.Effects)
                IniToggle(EffectIcon(effect.Key), effect.Title, effect.Hint, effect.Key);

            Ws.PortChanged += UpdateUpscaler;
        }

        private void IniSlider(Symbol icon, string title, double min, double max, string key, Panel? into = null,
            double step = 0.05, double fallback = 1.0) =>
            SliderRow(icon, title, null, min, max, step, () => ParseDouble(Ini.Get(key, Fmt(fallback)), fallback), v => Ini.Set(key, Fmt(v)), into: into);

        private static Symbol EffectIcon(string key) => key switch
        {
            "skip_intro" => Symbol.FastForward,
            "debug_camera" => Symbol.Camera,
            "debug_menu" => Symbol.Bug,
            _ => Symbol.Color,
        };

        public override void OnShown() => UpdateUpscaler();

        /// <summary>The engine has DLSS built in; NVIDIA's runtime sits beside it.</summary>
        private static bool DlssFound() => Paths.DlssLibrary != null;

        private void UpdateUpscaler()
        {
            var value = Ini.Get("upscaler");
            string? hint = value switch
            {
                "fsr4" => Directory.Exists(Path.Combine(Paths.PortDir, "fsr4_shaders"))
                    ? "Assets found. GPUs without the needed shader features fall back to FSR 3.1"
                    : "fsr4_shaders is missing from the engine folder: the game falls back to FSR 3.1",
                "fsr411" when Fsr4VkFound() =>
                    "Through fsr4vk (included, experimental): runs on any GPU with the needed Vulkan features. Prefer FSR 4 if it looks wrong",
                "fsr411" => Fsr411Problem() is { } problem
                    ? $"{problem}: the game falls back to FSR 4"
                    : "Assets for the chosen resolution and preset found",
                "dlss" => DlssFound()
                    ? "Needs an NVIDIA GeForce RTX GPU; others fall back to FSR 3.1" +
                      (Ini.Get("preset", "0") == "0" ? ". With Native AA this is DLAA: DLSS as anti-aliasing at full resolution" : "")
                    : "DLSS files are missing: the game falls back to FSR 3.1",
                "taa" => "Anti-aliasing at the output resolution, without an FSR model. Softer than DLAA or FSR Native AA: add sharpening below",
                _ => null,
            };
            SetHint(_upscalerHint, hint);
            _presetCard.IsEnabled = value is not ("taa" or "off");
            _sharpenCard.IsEnabled = value != "off";
            _sharpnessCard.IsEnabled = value != "off";

            var library = Paths.DlssLibrary;
            var fetch = Path.Combine(Paths.PortDir, "tools", "fetch_dlss.sh");
            _dlssDownload.IsVisible = library == null && !OperatingSystem.IsWindows() && File.Exists(fetch);
            SetHint(_dlssHint, library == null
                ? OperatingSystem.IsWindows()
                    ? "out\\nvngx_dlss.dll is missing from the engine folder"
                    : "NVIDIA's DLSS runtime is not here yet. Download takes it from NVIDIA's public DLSS SDK (needs an RTX GPU)"
                : OperatingSystem.IsWindows()
                    ? $"NVIDIA DLSS {System.Diagnostics.FileVersionInfo.GetVersionInfo(library).FileVersion} is included (out\\nvngx_dlss.dll). Choose it as the upscaler above"
                    : $"NVIDIA DLSS {Path.GetFileName(library)["libnvidia-ngx-dlss.so.".Length..]} is ready ({Path.GetDirectoryName(library)}). Choose it as the upscaler above");
        }

        /// <summary>The engine's tools/fetch_dlss.sh: NVIDIA's DLSS runtime into out/.</summary>
        private async Task DownloadDlssAsync()
        {
            _dlssDownload.IsEnabled = false;
            _dlssDownload.Label = "Downloading…";
            try
            {
                var info = new System.Diagnostics.ProcessStartInfo("bash")
                {
                    WorkingDirectory = Paths.PortDir, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
                };
                info.ArgumentList.Add(Path.Combine(Paths.PortDir, "tools", "fetch_dlss.sh"));
                info.ArgumentList.Add(Paths.DlssDir);
                using var process = System.Diagnostics.Process.Start(info)!;
                var output = await process.StandardOutput.ReadToEndAsync();
                var errors = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();
                foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries)) Ws.Log(line, LogLevel.Info);
                if (process.ExitCode != 0)
                {
                    Ws.Log("DLSS download failed: " + errors.Trim(), LogLevel.Bad);
                    Prompt.Tell("DLSS", "The download failed:\n" + errors.Trim());
                }
                else
                    Ws.Log("DLSS is ready: choose it as the upscaler.", LogLevel.Good);
            }
            catch (Exception e)
            {
                Prompt.Tell("DLSS", "Could not run tools/fetch_dlss.sh: " + e.Message);
            }
            _dlssDownload.Label = "Download";
            _dlssDownload.IsEnabled = true;
            UpdateUpscaler();
        }

        /// <summary>The fsr4vk provider the Windows package ships (FSR 4.1.1 on any GPU, vk_fsr4vk.cpp).</summary>
        private static bool Fsr4VkFound() =>
            File.Exists(Path.Combine(Paths.PortDir, "fsr4vk", "amd_fidelityfx_upscaler_vk.dll"));

        private static string Fsr411Dir()
        {
            var env = Environment.GetEnvironmentVariable("BB_FSR411_DIR");
            if (!string.IsNullOrEmpty(env)) return env;
            var local = Path.Combine(Paths.PortDir, "fsr4_411");
            return Directory.Exists(local) ? local : Path.Combine(Paths.DataDir, "fsr4_411");
        }

        /// <summary>The FSR 4.1.1 model files the chosen output and preset need (fsr4_411/).</summary>
        private static string? Fsr411Problem()
        {
            var (w, h) = Choices.OutputSize(Ini.Get("output_res", "1920x1080"));
            var model = (w > 1920 || h > 1080 ? "t2160" : "t1080") + (Ini.Get("preset", "4") == "4" ? "_m1" : "_m0");
            var passes = new List<string> { "spd", "prepass", "pass0_post" };
            for (var i = 1; i <= 12; i++)
            {
                passes.Add($"pass{i}");
                passes.Add($"pass{i}_post");
            }
            passes.Add("postpass");
            passes.Add("rcas");

            var dir = Path.Combine(Fsr411Dir(), model);
            foreach (var name in passes)
            {
                var file = new FileInfo(Path.Combine(dir, name + ".spv"));
                if (!file.Exists || file.Length < 20 || file.Length % 4 != 0)
                    return $"Missing or damaged fsr4_411/{model}/{name}.spv";
            }
            var init = new FileInfo(Path.Combine(dir, "initializer.bin"));
            return init.Exists && init.Length == 131072 ? null : $"Missing or damaged fsr4_411/{model}/initializer.bin";
        }
    }
}
