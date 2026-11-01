// TraderScarcity - Copyright (C) 2026 P_moT
// SPDX-License-Identifier: GPL-3.0-only

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using EFT;
using EFT.Hideout;
using EFT.InventoryLogic;
using EFT.UI;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TraderScarcity.Client;

/// What a ProduceView was shown with, so it can be re-shown with a sibling recipe.
public class ShowArgs
{
    public readonly ItemUiContext ItemUiContext;
    public readonly InventoryController InventoryController;
    public readonly ItemsProducerBase Producer;
    public readonly Action<string> OnStartProducing;
    public readonly Action<string> GetProducedItems;
    public readonly bool AvailableFavorites;

    public ShowArgs(ItemUiContext itemUiContext, InventoryController inventoryController, ItemsProducerBase producer, Action<string> onStartProducing, Action<string> getProducedItems, bool availableFavorites)
    {
        ItemUiContext = itemUiContext;
        InventoryController = inventoryController;
        Producer = producer;
        OnStartProducing = onStartProducing;
        GetProducedItems = getProducedItems;
        AvailableFavorites = availableFavorites;
    }
}

/// "< Input 2/5 >  < Output 1/3 >" strip on a recipe card. Switching re-shows the card with the matching sibling recipe,
/// so requirements, have/need counts and the Start button are all the game's own UI for a real recipe.
/// Locked while any recipe of the group is producing or has items waiting.
public class GroupPicker : MonoBehaviour
{
    private const string ObjectName = "TraderScarcityGroupPicker";
    private static readonly Color Background = new Color(0f, 0f, 0f, 0.65f);
    private static readonly Color ButtonColor = new Color(1f, 1f, 1f, 0.08f);
    private static readonly Color TextColor = new Color(0.86f, 0.84f, 0.76f);
    private static readonly Color LockedColor = new Color(0.5f, 0.5f, 0.5f);

    private static readonly AccessTools.FieldRef<ProduceView, Action<ProductionScheme>> FavoriteHandler =
        AccessTools.FieldRefAccess<ProduceView, Action<ProductionScheme>>("_onUpdateFavoriteSchemesList");

    private static readonly HashSet<GroupPicker> Live = new HashSet<GroupPicker>();

    private ProduceView _view;
    private ShowArgs _args;
    private string _group;
    private bool _switching;

    private RectTransform _rect;
    private Segment _input;
    private Segment _output;

    private bool _edges;
    private RectTransform _window;
    private readonly Vector3[] _corners = new Vector3[4];


    public static void Attach(ProduceView view, ShowArgs args, string group)
    {
        GroupPicker picker = Find(view) ?? Create(view);
        picker._args = args;
        picker._group = group;
        picker.gameObject.SetActive(true);
    }

    public static void Detach(ProduceView view)
    {
        GroupPicker picker = Find(view);
        if (picker != null)
        {
            picker._group = null;
            picker.gameObject.SetActive(false);
        }
    }

    public static void Refresh(ProduceView view)
    {
        GroupPicker picker = Find(view);
        if (picker != null)
        {
            picker.RefreshState();
        }
    }

    public static void RefreshAllLayouts()
    {
        foreach (GroupPicker picker in Live.Where(picker => picker != null))
        {
            picker.ApplyLayout();
        }
    }

    private static GroupPicker Find(ProduceView view)
    {
        return view == null ? null : view.transform.Find(ObjectName)?.GetComponent<GroupPicker>();
    }

    private static GroupPicker Create(ProduceView view)
    {
        GameObject root = new GameObject(ObjectName, typeof(RectTransform));
        root.transform.SetParent(view.transform, false);
        root.AddComponent<LayoutElement>().ignoreLayout = true; // Float over the card, don't push its layout around
        // Lays the two panels out side by side in the Corner layout; next to the controls they position themselves
        Row(root, spacing: 6f, padding: new RectOffset(0, 0, 0, 0));

        GroupPicker picker = root.AddComponent<GroupPicker>();
        picker._view = view;
        picker._rect = (RectTransform)root.transform;
        TMP_FontAsset font = view._productionStatus != null ? view._productionStatus.font : null;
        picker._input = new Segment(root.transform, font, "Input", dir => picker.Cycle(input: true, dir));
        picker._output = new Segment(root.transform, font, "Output", dir => picker.Cycle(input: false, dir));
        picker._input.Hover(() => picker.Describe(input: true));
        picker._output.Hover(() => picker.Describe(input: false));
        picker.ApplyLayout();
        Live.Add(picker);
        return picker;
    }

    private void OnDestroy()
    {
        Live.Remove(this);
    }

    private void ApplyLayout()
    {
        bool edges = Plugin.PickerLayout.Value == PickerLayout.Edges;
        // Corner: the root's row lays both panels out together and sizes itself to them
        GetComponent<HorizontalLayoutGroup>().enabled = !edges;
        GetComponent<ContentSizeFitter>().enabled = !edges;
        _input.Panel.GetComponent<LayoutElement>().ignoreLayout = edges;
        _output.Panel.GetComponent<LayoutElement>().ignoreLayout = edges;
        _edges = edges;
        if (edges)
        {
            // Cover the whole card, so (0, 0) here is the card's centre. LateUpdate pushes the panels out from it.
            _rect.anchorMin = Vector2.zero;
            _rect.anchorMax = Vector2.one;
            _rect.pivot = new Vector2(0.5f, 0.5f);
            _rect.offsetMin = Vector2.zero;
            _rect.offsetMax = Vector2.zero;
            PrepareEdgePanel(_input.Panel, left: true);
            PrepareEdgePanel(_output.Panel, left: false);
            PlaceAtWindowEdges();
        }
        else
        {
            Vector2 corner = Corner(Plugin.PickerCorner.Value);
            _rect.anchorMin = corner;
            _rect.anchorMax = corner;
            _rect.pivot = corner;
            _rect.anchoredPosition = Plugin.PickerOffset.Value;
        }
        _input.SetFontSize(Plugin.PickerFontSize.Value);
        _output.SetFontSize(Plugin.PickerFontSize.Value);
    }

    private static void PrepareEdgePanel(RectTransform panel, bool left)
    {
        // Anchored at the card's centre; the pivot on the outer side, so the panel grows inwards from the window edge
        panel.anchorMin = new Vector2(0.5f, 0.5f);
        panel.anchorMax = new Vector2(0.5f, 0.5f);
        panel.pivot = new Vector2(left ? 0f : 1f, 0.5f);
    }

    // Cards scroll, so follow the card vertically; horizontally the panels sit at the window's edges,
    // which don't move when switching options changes the recipe's length
    private void LateUpdate()
    {
        if (_edges)
        {
            PlaceAtWindowEdges();
        }
    }

    private void PlaceAtWindowEdges()
    {
        if (_view == null)
        {
            return;
        }
        if (_window == null)
        {
            _window = FindWindow(_view);
        }
        // Window edges in card-centred coordinates: offset from the card's centre in opposite directions
        _window.GetWorldCorners(_corners); // bottom-left, top-left, top-right, bottom-right
        float left = _rect.InverseTransformPoint(_corners[0]).x;
        float right = _rect.InverseTransformPoint(_corners[2]).x;
        Vector2 inset = Plugin.PickerEdgeInset.Value;
        _input.Panel.anchoredPosition = new Vector2(left + inset.x, inset.y);
        _output.Panel.anchoredPosition = new Vector2(right - inset.x, inset.y);
    }

    /// The visible area of the production list the card scrolls in - pickers beyond it would be clipped by its mask.
    /// A card that isn't in a list (Usable Crafting's details pane) is its own window.
    private static RectTransform FindWindow(ProduceView view)
    {
        ScrollRect scroll = view.GetComponentInParent<ScrollRect>(true);
        if (scroll != null)
        {
            return scroll.viewport != null ? scroll.viewport : (RectTransform)scroll.transform;
        }
        return (RectTransform)view.transform;
    }

    private static HorizontalLayoutGroup Row(GameObject go, float spacing, RectOffset padding)
    {
        HorizontalLayoutGroup row = go.AddComponent<HorizontalLayoutGroup>();
        row.spacing = spacing;
        row.padding = padding;
        row.childAlignment = TextAnchor.MiddleCenter;
        row.childControlWidth = true;
        row.childControlHeight = true;
        row.childForceExpandWidth = false;
        row.childForceExpandHeight = false;
        ContentSizeFitter fitter = go.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        return row;
    }

    private void RefreshState()
    {
        if (_group == null || _view == null || _view.Scheme == null || !CraftGroups.TryGet(_view.Scheme._id, out GroupRef current) || current.Group != _group)
        {
            gameObject.SetActive(false);
            return;
        }
        List<Sibling> siblings = CraftGroups.Siblings(_group, _args.Producer);
        List<int> inputs = siblings.Select(s => s.Ref.Input).Distinct().OrderBy(i => i).ToList();
        List<int> outputs = siblings.Select(s => s.Ref.Output).Distinct().OrderBy(o => o).ToList();
        bool locked = siblings.Any(s => CraftGroups.IsActive(_args.Producer, s.Scheme._id));
        _input.Set(inputs.IndexOf(current.Input) + 1, inputs.Count, locked);
        _output.Set(outputs.IndexOf(current.Output) + 1, outputs.Count, locked);
        gameObject.SetActive(inputs.Count > 1 || outputs.Count > 1);
    }

    private void Cycle(bool input, int direction)
    {
        if (_switching || _view == null || _view.Scheme == null || !CraftGroups.TryGet(_view.Scheme._id, out GroupRef current))
        {
            return;
        }
        List<Sibling> siblings = CraftGroups.Siblings(_group, _args.Producer);
        if (siblings.Any(s => CraftGroups.IsActive(_args.Producer, s.Scheme._id)))
        {
            return;
        }
        List<int> values = siblings.Select(s => input ? s.Ref.Input : s.Ref.Output).Distinct().OrderBy(v => v).ToList();
        if (values.Count < 2)
        {
            return;
        }
        int next = values[(values.IndexOf(input ? current.Input : current.Output) + direction + values.Count) % values.Count];
        //Keep the other side as it was, if that combination exists
        Sibling target = siblings.FirstOrDefault(s => input ? s.Ref.Input == next && s.Ref.Output == current.Output : s.Ref.Output == next && s.Ref.Input == current.Input);
        if (target.Scheme == null)
        {
            target = siblings.First(s => (input ? s.Ref.Input : s.Ref.Output) == next);
        }
        CraftGroups.Select(_group, target.Ref.Input, target.Ref.Output);
        Rebind(target.Scheme);
    }

    private void Rebind(ProductionScheme scheme)
    {
        _switching = true;
        try
        {
            // Close clears the favorite handler the panel subscribed - keep it, so starring a recipe still re-sorts the list
            Action<ProductionScheme> favoriteHandler = FavoriteHandler(_view);
            ShowArgs args = _args;
            _view.Close();
            _view.Show(args.ItemUiContext, args.InventoryController, scheme, args.Producer, args.OnStartProducing, args.GetProducedItems, args.AvailableFavorites);
            FavoriteHandler(_view) = favoriteHandler;
            // In Usable Crafting this card is the details pane - its grid tile should show the new output too
            UsableCraftingCompat.RefreshTile(_view, args);
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"Failed to switch recipe option: {ex}");
        }
        finally
        {
            _switching = false;
        }
    }

    /// Tooltip listing every option, the current one marked.
    private string Describe(bool input)
    {
        if (_view == null || _view.Scheme == null || !CraftGroups.TryGet(_view.Scheme._id, out GroupRef current))
        {
            return null;
        }
        List<Sibling> siblings = CraftGroups.Siblings(_group, _args.Producer);
        StringBuilder text = new StringBuilder(input ? "Input options:" : "Output options:");
        foreach (var option in siblings.GroupBy(s => input ? s.Ref.Input : s.Ref.Output).OrderBy(g => g.Key))
        {
            ProductionScheme scheme = option.First().Scheme;
            bool selected = option.Key == (input ? current.Input : current.Output);
            string line = input
                ? string.Join(" + ", scheme.RequiredItems.Select(item => $"{item.IntCount}x {item.ItemName}"))
                : $"{scheme.count}x {(scheme.endProduct + " ShortName").Localized()}";
            text.Append('\n').Append(selected ? "<color=#c5c3b2>> " : "<color=#8a8a8a>   ").Append(line).Append("</color>");
        }
        if (siblings.Any(s => CraftGroups.IsActive(_args.Producer, s.Scheme._id)))
        {
            text.Append("\n<color=#8a8a8a>Locked while crafting</color>");
        }
        return text.ToString();
    }

    private static Vector2 Corner(TextAnchor anchor)
    {
        switch (anchor)
        {
            case TextAnchor.UpperLeft: return new Vector2(0f, 1f);
            case TextAnchor.UpperCenter: return new Vector2(0.5f, 1f);
            case TextAnchor.MiddleLeft: return new Vector2(0f, 0.5f);
            case TextAnchor.MiddleCenter: return new Vector2(0.5f, 0.5f);
            case TextAnchor.MiddleRight: return new Vector2(1f, 0.5f);
            case TextAnchor.LowerLeft: return new Vector2(0f, 0f);
            case TextAnchor.LowerCenter: return new Vector2(0.5f, 0f);
            case TextAnchor.LowerRight: return new Vector2(1f, 0f);
            default: return new Vector2(1f, 1f);
        }
    }

    private static TextMeshProUGUI Text(Transform parent, TMP_FontAsset font, string value)
    {
        GameObject go = new GameObject("Text", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        TextMeshProUGUI text = go.AddComponent<TextMeshProUGUI>();
        if (font != null)
        {
            text.font = font;
        }
        text.text = value;
        text.color = TextColor;
        text.alignment = TextAlignmentOptions.Center;
        text.enableWordWrapping = false;
        text.raycastTarget = false;
        return text;
    }

    /// "< Label n/m >" on its own background, so input and output can sit in different places on the card
    private class Segment
    {
        public readonly RectTransform Panel;
        private readonly string _name;
        private readonly TextMeshProUGUI _label;
        private readonly TextMeshProUGUI[] _arrows;
        private readonly Button _previous;
        private readonly Button _next;
        private readonly Tooltip _tooltip;

        public Segment(Transform parent, TMP_FontAsset font, string name, Action<int> cycle)
        {
            _name = name;
            GameObject panel = new GameObject(name, typeof(RectTransform));
            panel.transform.SetParent(parent, false);
            panel.AddComponent<Image>().color = Background;
            panel.AddComponent<LayoutElement>();
            Row(panel, spacing: 2f, padding: new RectOffset(4, 4, 2, 2));
            Panel = (RectTransform)panel.transform;
            _previous = Arrow(Panel, font, "<", () => cycle(-1), out TextMeshProUGUI previousText);
            _label = Text(Panel, font, name);
            _label.raycastTarget = true; // for the tooltip
            _tooltip = _label.gameObject.AddComponent<Tooltip>();
            _next = Arrow(Panel, font, ">", () => cycle(1), out TextMeshProUGUI nextText);
            _arrows = [previousText, nextText];
        }

        public void Hover(Func<string> describe)
        {
            _tooltip.Describe = describe;
        }

        public void Set(int index, int count, bool locked)
        {
            Panel.gameObject.SetActive(count > 1);
            _label.text = $"{_name} {index}/{count}";
            _label.color = locked ? LockedColor : TextColor;
            _previous.interactable = !locked;
            _next.interactable = !locked;
            foreach (TextMeshProUGUI arrow in _arrows)
            {
                arrow.color = locked ? LockedColor : TextColor;
            }
        }

        public void SetFontSize(int size)
        {
            _label.fontSize = size;
            foreach (TextMeshProUGUI arrow in _arrows)
            {
                arrow.fontSize = size + 2;
            }
        }

        private static Button Arrow(Transform parent, TMP_FontAsset font, string glyph, Action onClick, out TextMeshProUGUI text)
        {
            GameObject go = new GameObject("Arrow", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            Image image = go.AddComponent<Image>();
            image.color = ButtonColor;
            Button button = go.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() => onClick());
            LayoutElement layout = go.AddComponent<LayoutElement>();
            layout.minWidth = 18f;
            layout.minHeight = 18f;
            text = Text(go.transform, font, glyph);
            RectTransform textRect = (RectTransform)text.transform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
            return button;
        }
    }

    /// Shows the game's own tooltip on hover.
    private class Tooltip : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public Func<string> Describe;

        // Only close the shared tooltip if we opened it
        private bool _showing;

        public void OnPointerEnter(PointerEventData eventData)
        {
            string text = Describe?.Invoke();
            SimpleTooltip tooltip = ItemUiContext.Instance != null ? ItemUiContext.Instance.Tooltip : null;
            if (!string.IsNullOrEmpty(text) && tooltip != null)
            {
                tooltip.Show(text);
                _showing = true;
            }
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            Hide();
        }

        private void OnDisable()
        {
            Hide();
        }

        private void Hide()
        {
            if (!_showing)
            {
                return;
            }
            _showing = false;
            SimpleTooltip tooltip = ItemUiContext.Instance != null ? ItemUiContext.Instance.Tooltip : null;
            if (tooltip != null)
            {
                tooltip.Close();
            }
        }
    }
}
