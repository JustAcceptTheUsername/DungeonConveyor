using UnityEngine;
using UnityEngine.InputSystem;

namespace DungeonConveyor
{
    /// <summary>
    /// The settings window, opened from the main menu or the in-game Settings button: audio volumes (master, effects,
    /// music, ambience, interface) and mute for the FMOD sounds (see Sfx.Settings), reduced motion, and whether the tutorial
    /// shows at the start of a run. Changes apply and save at once. One instance lives across scenes and draws on top
    /// of everything; the game pauses while it is open (HUD) and clicks don't reach what's behind it.
    /// </summary>
    public class SettingsMenu : MonoBehaviour
    {
        static SettingsMenu instance;

        public static bool IsOpen => instance != null && instance.open;

        public static void Open()
        {
            if (instance == null)
            {
                var go = new GameObject("Settings Menu");
                DontDestroyOnLoad(go);
                instance = go.AddComponent<SettingsMenu>();
            }
            if (!instance.open) Sfx.Play(Sfx.Lib.OpenWindow);
            instance.open = true;
        }

        public static void Close()
        {
            if (instance == null) return;
            instance.open = false;
            Sfx.Play(Sfx.Lib.CloseWindow);
            Sfx.Settings.Save();
            PlayerPrefs.Save();
        }

        bool open;
        int dragging = -1;

        void Update()
        {
            var k = Keyboard.current;
            if (open && k != null && k.escapeKey.wasPressedThisFrame) Close();
        }

        void OnGUI()
        {
            if (!open) return;
            GUI.depth = -100; // above the HUD, the tutorial and the menus
            MenuGui.Begin();
            MenuGui.Dim(0.6f);

            const float w = 780f, h = 760f;
            float x = (MenuGui.VW - w) * 0.5f, y = (1080f - h) * 0.5f;
            MenuGui.Panel(MenuGui.R(x, y, w, h));
            MenuGui.Text(MenuGui.R(x, y + 30f, w, 60f), "SETTINGS", MenuGui.Title, UIStyle.Accent);

            float row = y + 110f;
            MenuGui.Text(MenuGui.R(x + 60f, row, 160f, 30f), "AUDIO", MenuGui.Label, UIStyle.Muted);
#if !FMOD_PRESENT
            MenuGui.Text(MenuGui.R(x + 200f, row, w - 260f, 30f), "FMOD for Unity isn't installed yet: the game is silent", MenuGui.Small, UIStyle.Danger);
#endif
            row += 44f;
            Slider(0, "Master", Sfx.Settings.Master, v => Sfx.Settings.Master = v, x, row, w); row += 62f;
            Slider(1, "Effects", Sfx.Settings.Effects, v => Sfx.Settings.Effects = v, x, row, w); row += 62f;
            Slider(4, "Music", Sfx.Settings.Music, v => Sfx.Settings.Music = v, x, row, w); row += 62f;
            Slider(2, "Ambience", Sfx.Settings.Ambience, v => Sfx.Settings.Ambience = v, x, row, w); row += 62f;
            Slider(3, "Interface", Sfx.Settings.Interface, v => Sfx.Settings.Interface = v, x, row, w); row += 68f;
            if (MenuGui.Toggle(MenuGui.R(x + 60f, row, 40f, 40f), "Mute all sound", Sfx.Settings.Mute, 600f * MenuGui.S)) Sfx.Settings.Mute = !Sfx.Settings.Mute;
            row += 66f;

            MenuGui.Text(MenuGui.R(x + 60f, row, w - 120f, 30f), "GAME", MenuGui.Label, UIStyle.Muted);
            row += 44f;
            if (MenuGui.Toggle(MenuGui.R(x + 60f, row, 40f, 40f), "Reduced motion (no screen shake or fly-ins)", HudFx.ReducedMotion, 600f * MenuGui.S))
                HudFx.ReducedMotion = !HudFx.ReducedMotion;
            row += 54f;
            if (MenuGui.Toggle(MenuGui.R(x + 60f, row, 40f, 40f), "Show the tutorial at the start of a run", Tutorial.ShowAtStart, 600f * MenuGui.S))
                Tutorial.ShowAtStart = !Tutorial.ShowAtStart;

            if (MenuGui.Button(MenuGui.R(x + (w - 260f) * 0.5f, y + h - 92f, 260f, 60f), "CLOSE")) Close();
            MenuGui.SwallowClicks();
        }

        void Slider(int id, string name, float value, System.Action<float> set, float x, float y, float w)
        {
            float s = MenuGui.S;
            MenuGui.Text(MenuGui.R(x + 60f, y, 170f, 40f), name, MenuGui.Label, UIStyle.Parchment);
            var track = MenuGui.R(x + 240f, y + 14f, w - 400f, 12f);
            var hit = new Rect(track.x - 10f * s, track.y - 16f * s, track.width + 20f * s, track.height + 32f * s);
            var e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0 && hit.Contains(e.mousePosition)) dragging = id;
            if (dragging == id && (e.type == EventType.MouseDrag || e.type == EventType.MouseDown))
            {
                value = Mathf.Clamp01((e.mousePosition.x - track.x) / track.width);
                set(value);
                e.Use();
            }
            if (dragging == id && e.type == EventType.MouseUp)
            {
                dragging = -1;
                // Hear the new level.
                // Music and ambience are heard as they play; the others play a sample.
                if (id == 3) Sfx.Play(Sfx.Lib.Click);
                else if (id < 2) Sfx.Play(Sfx.Lib.SliderPreview.IsSet ? Sfx.Lib.SliderPreview : Sfx.Lib.CrystalSold);
                e.Use();
            }

            if (e.type != EventType.Repaint) return;
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(track, UIStyle.White);
            GUI.color = UIStyle.Accent;
            GUI.DrawTexture(new Rect(track.x, track.y, track.width * value, track.height), UIStyle.White);
            GUI.color = dragging == id ? UIStyle.Accent : UIStyle.Parchment;
            float hx = track.x + track.width * value;
            GUI.DrawTexture(new Rect(hx - 9f * s, track.center.y - 13f * s, 18f * s, 26f * s), UIStyle.White);
            GUI.color = Color.white;
            MenuGui.Text(MenuGui.R(x + w - 150f, y, 90f, 40f), Mathf.RoundToInt(value * 100f) + "%", MenuGui.Label, UIStyle.Parchment);
        }
    }
}
