using System.Text;
using TMPro;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// The inventory window of the selected unit (the I key opens and closes it): the six slots with what is in them, the weapon in the hands
/// marked, how full the pockets of the vest and the backpack are, and what is in them. It only shows; what a unit carries is changed through
/// UnitInventory. The window fades with a CanvasGroup, is hidden in the shot view and follows the selected unit (and the changes in its inventory).
/// </summary>
public class InventoryPanel : MonoBehaviour
{
    [SerializeField] private UnitSelector selector;
    [SerializeField] private CanvasGroup group;
    [SerializeField] private TMP_Text text;
    [SerializeField] private bool startOpen;

    private bool open;
    private string shown;

    private void Awake()
    {
        open = startOpen;
        if (group != null)
        {
            group.alpha = open ? 1f : 0f;
            group.blocksRaycasts = false;
            group.interactable = false;
        }
    }

    private void Update()
    {
        if (selector == null)
            selector = FindFirstObjectByType<UnitSelector>();

        if (KeyPressed())
            open = !open;

        Unit unit = selector != null ? selector.SelectedUnit : null;
        if (unit != null && !unit.IsSelected)
            unit = null;

        bool visible = open && unit != null && !ShotCamera.Active;
        if (group != null)
            group.alpha = Mathf.MoveTowards(group.alpha, visible ? 1f : 0f, 6f * Time.unscaledDeltaTime);

        if (visible && text != null)
        {
            string content = Describe(unit);
            if (content != shown)
            {
                shown = content;
                text.text = content;
            }
        }
    }

    /// <summary>The text of the window for this unit.</summary>
    public static string Describe(Unit unit)
    {
        UnitInventory inv = unit.Inventory;
        StringBuilder sb = new StringBuilder();
        sb.Append("<b>СНАРЯЖЕНИЕ</b>   <size=70%>" + unit.name + "  (I - закрыть)</size>\n\n");

        foreach (InventorySlot slot in InventorySlots.All)
        {
            ItemData item = inv.Get(slot);
            bool inHands = InventorySlots.IsWeaponSlot(slot) && slot == inv.ActiveWeaponSlot && item != null;

            sb.Append(inHands ? "<color=#FFD24A>" : "");
            sb.Append(InventorySlots.Label(slot) + ": ");
            sb.Append(item != null ? item.DisplayName : "-");

            if (item is ArmorData)
                sb.Append("  <size=75%>(урон -" + ((ArmorData)item).DamageReduction + ")</size>");
            if (slot == InventorySlot.Vest && item != null)
                sb.Append("  <size=75%>(" + inv.VestItems.Count + "/" + inv.VestCapacity + ")</size>");
            if (slot == InventorySlot.Backpack && item != null)
                sb.Append("  <size=75%>(" + inv.BackpackItems.Count + "/" + inv.BackpackCapacity + ")</size>");
            if (inHands)
                sb.Append("  <size=75%>[в руках]</size>");
            sb.Append(inHands ? "</color>" : "");
            sb.Append('\n');

            if (slot == InventorySlot.Vest)
                AppendContents(sb, inv.VestItems);
            else if (slot == InventorySlot.Backpack)
                AppendContents(sb, inv.BackpackItems);
        }

        return sb.ToString().TrimEnd('\n');
    }

    private static void AppendContents(StringBuilder sb, System.Collections.Generic.IReadOnlyList<ItemData> items)
    {
        foreach (ItemData i in items)
            sb.Append("<size=80%>     - " + i.DisplayName + "</size>\n");
    }

    private static bool KeyPressed()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && Keyboard.current.iKey.wasPressedThisFrame)
            return true;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetKeyDown(KeyCode.I))
            return true;
#endif
        return false;
    }
}
