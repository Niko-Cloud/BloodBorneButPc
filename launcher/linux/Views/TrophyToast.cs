using Avalonia.Media;
using BloodborneLauncher.Core;

namespace BloodborneLauncher.Views
{
    /// <summary>
    /// The trophy notification. The Windows launcher draws its own click-through window in a corner
    /// of the game's monitor; Wayland lets no program put a window over another one's fullscreen
    /// window, so here it is a desktop notification (notify-send: KDE, GNOME and the others show
    /// it over the game), with the same sound.
    /// </summary>
    public static class TrophyToast
    {
        public static Color GradeColor(Grade grade) => grade switch
        {
            Grade.Platinum => Color.FromRgb(0xB9, 0xD3, 0xEE),
            Grade.Gold => Color.FromRgb(0xE3, 0xB3, 0x41),
            Grade.Silver => Color.FromRgb(0xC4, 0xC8, 0xD0),
            _ => Color.FromRgb(0xC8, 0x84, 0x4B),
        };

        /// <summary>Shows a notification (and plays its sound) for an unlocked trophy.</summary>
        public static void Announce(Trophy trophy)
        {
            var s = Workspace.Current.Settings;
            if (s.Bool("trophy_sound"))
                Chime.Play(s.Int("trophy_volume"), s.Str("trophy_sound_file"));
            try
            {
                var info = new System.Diagnostics.ProcessStartInfo("notify-send") { UseShellExecute = false };
                foreach (var argument in new[]
                         {
                             "--app-name=Bloodborne", "--icon=trophy-gold", "--expire-time=6000",
                             $"{trophy.Name} ({trophy.Grade})", "You have earned a trophy!",
                         })
                    info.ArgumentList.Add(argument);
                System.Diagnostics.Process.Start(info)?.Dispose();
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // No notify-send (libnotify): the activity log still has it.
            }
        }
    }
}
