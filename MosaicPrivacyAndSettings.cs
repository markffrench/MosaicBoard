using Framework;
using Helpers;
using UnityEngine;

namespace Board
{
    /// <summary>
    /// Settings shared across all Divide the Plunder mosaic puzzle games.
    /// Game-specific settings live in each project's own ProjectPrivacyAndSettings.
    /// </summary>
    public static class MosaicPrivacyAndSettings
    {
        private const string HintsEnabledKey        = "hints_enabled";
        private const string ShowErrorsKey           = "show_errors";
        private const string ColorSchemeKey          = "color_scheme";
        private const string ClickBehaviourKey       = "click_behaviour";
        private const string InvertedInputKey        = "inverted_input";
        private const string HighlightBrightnessKey  = "highlight_brightness";
        private const string EdgeScrollKey           = "edge_scroll";
        private const string MobileControlSchemeKey  = "mobile_control_scheme";
        private const string ZoomSensitivityKey      = "zoom_sensitivity";
        private const string DifficultyKey           = "difficulty";
        private const string CountdownModeKey        = "countdown_mode";
        private const string ShimmerEnabledKey       = "shimmer_enabled";
        private const string DebugPanelVisibleKey    = "debug_panel_visible";

        // Zoom sensitivity normalised defaults (0–1 range)
        public const float ZoomSensitivityDefaultPC  = 0.7f;
        public const float ZoomSensitivityDefaultMac = 0.3f;

        private static int colorSetting = -1;

        // ── Hints ──────────────────────────────────────────────────────────────

        public static bool GetHintsSetting() => SettingsRepository.GetBool(HintsEnabledKey, false);
        public static void SetHintsSetting(bool setting) => SettingsRepository.SetBool(HintsEnabledKey, setting);

        // ── Colour scheme ──────────────────────────────────────────────────────

        public static bool GetInvertedColors()
        {
            if (colorSetting == -1)
                colorSetting = SettingsRepository.GetInt(ColorSchemeKey, 0);

            return colorSetting == 1;
        }

        public static void SetInvertedColors(bool inverted)
        {
            SettingsRepository.SetInt(ColorSchemeKey, inverted ? 1 : 0);
            colorSetting = inverted ? 1 : 0;
        }

        // ── Errors ─────────────────────────────────────────────────────────────

        public static bool GetShowErrors() => SettingsRepository.GetBool(ShowErrorsKey, true);
        public static void SetShowErrors(bool enabled) => SettingsRepository.SetBool(ShowErrorsKey, enabled);

        // ── Click behaviour ────────────────────────────────────────────────────

        public static ClickBehaviour GetClickBehaviour()
        {
            if (Defines.IsMobile())
                return ClickBehaviour.Cycle;

            return (ClickBehaviour)SettingsRepository.GetInt(ClickBehaviourKey, 0);
        }

        public static void SetClickBehaviour(ClickBehaviour behaviour) =>
            SettingsRepository.SetInt(ClickBehaviourKey, (int)behaviour);

        // ── Input ──────────────────────────────────────────────────────────────

        public static bool GetInvertedInput() => SettingsRepository.GetBool(InvertedInputKey, false);
        public static void SetInvertedInput(bool inverted) => SettingsRepository.SetBool(InvertedInputKey, inverted);

        // ── Highlight brightness ───────────────────────────────────────────────

        public static float GetHighlightBrightness() => SettingsRepository.GetFloat(HighlightBrightnessKey, 0.05f);
        public static void SetHighlightBrightness(float brightness) => SettingsRepository.SetFloat(HighlightBrightnessKey, brightness);

        // ── Edge scroll ────────────────────────────────────────────────────────

        public static bool GetEdgeScrollEnabled() => SettingsRepository.GetBool(EdgeScrollKey, false);
        public static void SetEdgeScrollEnabled(bool enabled) => SettingsRepository.SetBool(EdgeScrollKey, enabled);

        // ── Mobile control scheme ──────────────────────────────────────────────

        public static MobileControlScheme GetMobileControlScheme() =>
            (MobileControlScheme)SettingsRepository.GetInt(MobileControlSchemeKey, 0);

        public static void SetMobileControlScheme(MobileControlScheme scheme) =>
            SettingsRepository.SetInt(MobileControlSchemeKey, (int)scheme);

        // ── Zoom sensitivity ───────────────────────────────────────────────────

        public static float GetZoomSensitivity()
        {
            float defaultValue = Defines.IsMacOS() ? ZoomSensitivityDefaultMac : ZoomSensitivityDefaultPC;
            return SettingsRepository.GetFloat(ZoomSensitivityKey, defaultValue);
        }

        public static void SetZoomSensitivity(float normalizedValue) =>
            SettingsRepository.SetFloat(ZoomSensitivityKey, normalizedValue);

        // ── Difficulty ─────────────────────────────────────────────────────────

        public static int GetDifficulty() => SettingsRepository.GetInt(DifficultyKey, -1);
        public static void SetDifficulty(int difficulty) => SettingsRepository.SetInt(DifficultyKey, difficulty);

        // ── Countdown mode ─────────────────────────────────────────────────────

        public static bool GetCountdownMode() => SettingsRepository.GetBool(CountdownModeKey, false);
        public static void SetCountdownMode(bool enabled) => SettingsRepository.SetBool(CountdownModeKey, enabled);

        // ── Shimmer ────────────────────────────────────────────────────────────

        public static bool GetShimmerEnabled() => SettingsRepository.GetBool(ShimmerEnabledKey, false);
        public static void SetShimmerEnabled(bool enabled) => SettingsRepository.SetBool(ShimmerEnabledKey, enabled);

        // ── Debug panel ────────────────────────────────────────────────────────

        public static bool GetDebugPanelVisible() => SettingsRepository.GetBool(DebugPanelVisibleKey, true);
        public static void SetDebugPanelVisible(bool visible) => SettingsRepository.SetBool(DebugPanelVisibleKey, visible);
    }
}
