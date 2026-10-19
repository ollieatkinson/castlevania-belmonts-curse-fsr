using Doozy.Runtime.Common.Events;
using Doozy.Runtime.UIManager.Components;
using HarmonyLib;
using Il2CppInterop.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Utilities.Localization;
using Object = UnityEngine.Object;

namespace BelmontsCurseGraphics;

// Append one choice without adding an invalid value to the game's saved enum.
internal static class NativeVideoMenu
{
    private const string AaPath = "video/antialiasing";
    private static readonly Dictionary<int, Selector> selectors = new();
    private static readonly Dictionary<int, Menu> menus = new();
    private static bool updating;
    internal static bool IsAa(UIDropdownEnumSettingBinding binding) => binding.SettingPath == AaPath;

    internal static void PrepareAa(UIDropdownEnumSettingBinding binding)
    {
        if (!IsAa(binding) || !binding._listStepper || binding.indexes == null || binding.indexes.Count == 0) return;
        if (!selectors.TryGetValue(binding.GetInstanceID(), out var selector))
        {
            selector = new Selector(binding);
            selectors.Add(binding.GetInstanceID(), selector);
            Plugin.Logger.LogInfo("Added FSR 3.1 Native AA to the native Video AA selector.");
        }
        selector.Refresh();
    }

    internal static void RefreshAa(UIDropdownEnumSettingBinding binding)
    {
        if (!updating && IsAa(binding) && selectors.TryGetValue(binding.GetInstanceID(), out var selector)) selector.Refresh();
    }

    internal static bool ChangeAa(UIDropdownEnumSettingBinding binding, int index)
    {
        if (!IsAa(binding) || !selectors.TryGetValue(binding.GetInstanceID(), out var selector)) return true;
        if (updating || Fsr31Controller.Instance is not { } owner) return false;
        if (Fsr31MenuModel.IsFsrChoice(index, selector.NativeValues.Length))
        {
            owner.Apply(owner.Current with { Enabled = true });
            selector.Refresh();
            return false;
        }
        if (!Fsr31MenuModel.IsNativeChoice(index, selector.NativeValues.Length)) return false;
        // Restore first, then let the original binding update and save its native value.
        owner.Apply(owner.Current with { Enabled = false });
        return true;
    }

    internal static void Construct(VideoSettingsView view)
    {
        if (!view || menus.ContainsKey(view.GetInstanceID())) return;
        var source = view.GetComponentsInChildren<UIDropdownEnumSettingBinding>(true).FirstOrDefault(IsAa);
        if (!source || !source!._listStepper || !source.nameLabel) return;
        PrepareAa(source);
        menus.Add(view.GetInstanceID(), new Menu(view, source));
        Plugin.Logger.LogInfo("Added FSR 3.1 sharpening and anti-ghosting controls to the native Video menu.");
    }

    internal static void Tick()
    {
        foreach (var id in selectors.Where(x => !x.Value.Binding).Select(x => x.Key).ToArray()) selectors.Remove(id);
        foreach (var id in menus.Where(x => !x.Value.View).Select(x => x.Key).ToArray()) menus.Remove(id);
        foreach (var selector in selectors.Values) selector.Refresh();
        foreach (var menu in menus.Values) menu.Refresh();
    }

    internal static void Shutdown()
    {
        foreach (var menu in menus.Values) menu.Dispose();
        foreach (var selector in selectors.Values) selector.Restore();
        menus.Clear(); selectors.Clear();
    }

    private static void SetChoices(UIListStepper stepper, IEnumerable<string> labels)
    {
        var choices = new Il2CppSystem.Collections.Generic.List<TMP_Dropdown.OptionData>();
        foreach (var label in labels) choices.Add(new TMP_Dropdown.OptionData(label));
        stepper.dropdown.options = choices;
        stepper.dropdown.RefreshShownValue();
    }

    private sealed class Selector
    {
        internal readonly UIDropdownEnumSettingBinding Binding;
        internal readonly int[] NativeValues;
        private readonly string[] labels;
        private readonly LocalizeDropdown? localizer;
        private readonly bool localizationEnabled, autoSize;
        private readonly float minSize, maxSize;
        private string fsrLabel = "";

        internal Selector(UIDropdownEnumSettingBinding binding)
        {
            Binding = binding;
            NativeValues = binding.indexes.ToArray();
            if (binding._listStepper.dropdown.options.Count != NativeValues.Length)
                throw new InvalidOperationException("Native AA labels do not match its enum values.");
            labels = Enumerable.Range(0, NativeValues.Length).Select(i => binding._listStepper.dropdown.options[i].text).ToArray();
            localizer = binding._listStepper.dropdown.GetComponent<LocalizeDropdown>();
            localizationEnabled = localizer && localizer!.enabled;
            if (localizer) localizer!.enabled = false;
            var caption = binding._listStepper.dropdown.captionText;
            autoSize = caption.enableAutoSizing; minSize = caption.fontSizeMin; maxSize = caption.fontSizeMax;
            caption.fontSizeMax = caption.fontSize; caption.fontSizeMin = caption.fontSize * .55f; caption.enableAutoSizing = true;
        }

        internal void Refresh()
        {
            if (!Binding || Fsr31Controller.Instance is not { } owner) return;
            var label = owner.Current.Enabled && (!Fsr31Backend.Available || owner.UnavailableReason != null) ? "FSR 3.1 (unavailable)" : "FSR 3.1 Native AA";
            updating = true;
            try
            {
                if (label != fsrLabel || Binding._listStepper.dropdown.options.Count != NativeValues.Length + 1)
                {
                    SetChoices(Binding._listStepper, labels.Append(label)); fsrLabel = label;
                }
                Binding._listStepper.SetValueWithoutNotify(Fsr31MenuModel.DisplayIndex(owner.Current.Enabled,
                    Binding._setting?.value ?? NativeValues[0], NativeValues));
            }
            finally { updating = false; }
        }

        internal void Restore()
        {
            if (!Binding) return;
            SetChoices(Binding._listStepper, labels);
            Binding._listStepper.SetValueWithoutNotify(Fsr31MenuModel.DisplayIndex(false, Binding._setting?.value ?? NativeValues[0], NativeValues));
            if (localizer) localizer!.enabled = localizationEnabled;
            var caption = Binding._listStepper.dropdown.captionText;
            caption.enableAutoSizing = autoSize; caption.fontSizeMin = minSize; caption.fontSizeMax = maxSize;
        }
    }

    private sealed class Menu
    {
        internal readonly VideoSettingsView View;
        private readonly List<GameObject> rows = new();
        private readonly UIListStepper sharpness, reactive;

        internal Menu(VideoSettingsView view, UIDropdownEnumSettingBinding source)
        {
            View = view;
            var template = source.transform;
            while (!source._listStepper.transform.IsChildOf(template) || !source.nameLabel.transform.IsChildOf(template))
                template = template.parent ? template.parent : throw new InvalidOperationException("Could not locate the native AA row.");
            var parent = template.parent;
            var staging = new GameObject("FSR settings staging");
            staging.SetActive(false); staging.transform.SetParent(parent, false);
            try
            {
                sharpness = CreateRow(template.gameObject, staging.transform, "FSR 3.1 sharpening",
                    Enumerable.Range(0, 11).Select(i => i == 0 ? "Off" : i * 10 + "%"),
                    index => { if (index is >= 0 and <= 10 && Fsr31Controller.Instance is { } owner) owner.Apply(owner.Current with { Sharpness = index / 10f }); },
                    () => { if (Fsr31Controller.Instance is { } owner) owner.Apply(owner.Current with { Sharpness = .2f }); });
                reactive = CreateRow(template.gameObject, staging.transform, "FSR 3.1 anti-ghosting", new[] { "Off", "On" },
                    index => { if (index is 0 or 1 && Fsr31Controller.Instance is { } owner) owner.Apply(owner.Current with { ReactiveMask = index == 1 }); },
                    () => { if (Fsr31Controller.Instance is { } owner) owner.Apply(owner.Current with { ReactiveMask = true }); });
                var positions = new List<float>();
                for (var i = 0; i < parent.childCount; i++)
                {
                    var child = parent.GetChild(i).TryCast<RectTransform>();
                    if (child && child!.gameObject.GetComponentInChildren<UISelectable>(true)) positions.Add(child.anchoredPosition.y);
                }
                positions.Sort();
                var original = template.Cast<RectTransform>();
                var step = positions.Zip(positions.Skip(1), (a, b) => b - a).Where(g => g > 1).DefaultIfEmpty(Math.Max(Math.Abs(original.sizeDelta.y), 50)).Min();
                for (var i = 0; i < rows.Count; i++)
                {
                    rows[i].transform.SetParent(parent, false);
                    if (!parent.GetComponent<VerticalLayoutGroup>() && positions.Count > 0)
                        rows[i].transform.Cast<RectTransform>().anchoredPosition = new Vector2(original.anchoredPosition.x, positions[0] - step * (i + 1));
                    rows[i].SetActive(true);
                }
                var group = View.navigationGroup;
                if (group)
                {
                    group.selectables = (group.selectables ?? Array.Empty<UISelectable>()).Where(s => s).Concat(new UISelectable[] { sharpness, reactive }).ToArray();
                    group.UpdateNavigation();
                }
                Refresh();
            }
            catch { foreach (var row in rows) Object.Destroy(row); throw; }
            finally { Object.Destroy(staging); }
        }

        private UIListStepper CreateRow(GameObject template, Transform parent, string label, IEnumerable<string> choices, Action<int> change, Action reset)
        {
            var row = Object.Instantiate(template, parent, false); rows.Add(row); row.name = label;
            var binding = row.GetComponentInChildren<UIDropdownEnumSettingBinding>(true);
            binding.enabled = false; binding.settingPath = "fsr31/" + label;
            foreach (var localizer in row.GetComponentsInChildren<UnityEngine.Localization.Components.LocalizeStringEvent>(true)) localizer.enabled = false;
            foreach (var localizer in row.GetComponentsInChildren<LocalizeDropdown>(true)) localizer.enabled = false;
            binding.nameLabel.text = label;
            var stepper = binding._listStepper;
            stepper.OnIndexChanged = new IntEvent(); SetChoices(stepper, choices);
            stepper.OnIndexChanged.AddListener((UnityAction<int>)(index => { if (!updating) change(index); }));
            foreach (var resetter in row.GetComponentsInChildren<UISettingResetter>(true))
            {
                resetter.onReset = new UnityEvent();
                resetter.onReset.AddListener((UnityAction)(() => reset()));
            }
            Object.Destroy(binding);
            return stepper;
        }

        internal void Refresh()
        {
            if (!View || Fsr31Controller.Instance is not { } owner) return;
            updating = true;
            try
            {
                sharpness.SetValueWithoutNotify(Fsr31MenuModel.SharpnessIndex(owner.Current.Sharpness));
                reactive.SetValueWithoutNotify(owner.Current.ReactiveMask ? 1 : 0);
            }
            finally { updating = false; }
        }

        internal void Dispose()
        {
            if (View && View.navigationGroup)
            {
                var group = View.navigationGroup;
                group.selectables = group.selectables.Where(s => s && s != sharpness && s != reactive).ToArray(); group.UpdateNavigation();
            }
            foreach (var row in rows) if (row) Object.Destroy(row);
        }
    }

}

[HarmonyPatch(typeof(VideoSettingsView), nameof(VideoSettingsView.ConstructUI))]
[HarmonyWrapSafe]
internal static class VideoMenuConstructed
{
    private static void Postfix(VideoSettingsView __instance)
    {
        try { NativeVideoMenu.Construct(__instance); }
        catch (Exception e) { Plugin.Logger.LogError($"Could not add FSR Video controls: {e}"); }
    }
}

[HarmonyPatch(typeof(UIDropdownEnumSettingBinding), nameof(UIDropdownEnumSettingBinding.Bind))]
[HarmonyWrapSafe]
internal static class AaSelectorBound
{
    private static void Postfix(UIDropdownEnumSettingBinding __instance) => NativeVideoMenu.PrepareAa(__instance);
}

[HarmonyPatch(typeof(UIDropdownEnumSettingBinding), nameof(UIDropdownEnumSettingBinding.Refresh))]
[HarmonyWrapSafe]
internal static class AaSelectorRefreshed
{
    private static void Postfix(UIDropdownEnumSettingBinding __instance) => NativeVideoMenu.RefreshAa(__instance);
}

[HarmonyPatch(typeof(UIDropdownEnumSettingBinding), nameof(UIDropdownEnumSettingBinding.OnValueChanged))]
[HarmonyWrapSafe]
internal static class AaSelectorChanged
{
    private static bool Prefix(UIDropdownEnumSettingBinding __instance, int index) => NativeVideoMenu.ChangeAa(__instance, index);
    private static void Postfix(UIDropdownEnumSettingBinding __instance) => NativeVideoMenu.RefreshAa(__instance);
}

[HarmonyPatch(typeof(VideoSettingsView), nameof(VideoSettingsView.ResetSettings))]
[HarmonyWrapSafe]
internal static class VideoMenuReset
{
    private static void Postfix()
    {
        Fsr31Controller.Instance?.Apply(new Fsr31Options()); NativeVideoMenu.Tick();
    }
}

[HarmonyPatch(typeof(UISettingResetter), nameof(UISettingResetter.Reset))]
[HarmonyWrapSafe]
internal static class NativeAaReset
{
    private static void Postfix(UISettingResetter __instance)
    {
        var binding = __instance.GetComponentInChildren<UIDropdownEnumSettingBinding>(true);
        if (!binding || !NativeVideoMenu.IsAa(binding!) || Fsr31Controller.Instance is not { } owner) return;
        owner.Apply(owner.Current with { Enabled = false }); NativeVideoMenu.RefreshAa(binding!);
    }
}
