using Dalamud.Game.Addon.Events;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.Internal.Classes;
using Lumina.Excel.Sheets;

namespace KamiToolKit.Nodes;

public class ItemIconNode : IconNode
{
    /// <summary>
    ///     Gets or sets the displayed item.
    /// </summary>
    public uint ItemID
    {
        get;
        set
        {
            if (field == value) 
                return;
            if (!IDataManager.Get().GetExcelSheet<Item>().TryGetRow(value, out var row))
                return;
            
            field       = value;
            IconId      = row.Icon;
            ItemTooltip = row.RowId;
        }
    }
    
    /// <summary>
    ///     Action that is triggered when the item is being clicked.
    /// </summary>
    public AtkEventListener.Delegates.ReceiveEvent? OnClick { get; set; }

    public unsafe ItemIconNode()
    {
        AddNodeFlags(NodeFlags.HasCollision);
        
        AddEvent
        (
            AtkEventType.MouseOver,
            () => IAddonEventManager.Get().SetCursor(AddonCursorType.Clickable)
        );
        
        AddEvent
        (
            AtkEventType.MouseOut,
            () => IAddonEventManager.Get().ResetCursor()
        );
            
        AddEvent
        (
            AtkEventType.MouseClick,
            MouseClickHandler
        );
    }

    private unsafe void MouseClickHandler
    (
        AtkEventListener* thisPtr,
        AtkEventType      eventType,
        int               eventParam,
        AtkEvent*         atkEvent,
        AtkEventData*     atkEventData
    )
        => OnClick?.Invoke(thisPtr, eventType, eventParam, atkEvent, atkEventData);
}
