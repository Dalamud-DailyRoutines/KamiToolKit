﻿using System.Drawing;
using System.Numerics;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Interface;
using Dalamud.Utility;
using Dalamud.Utility.Numerics;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.Extensions;
using KamiToolKit.Nodes.NodeStyles;

namespace KamiToolKit.Nodes;

public unsafe class TextNode : NodeBase<AtkTextNode> {

    private readonly Utf8String* stringBuffer = Utf8String.CreateEmpty();

    public TextNode() : base(NodeType.Text) {
        TextColor = KnownColor.White.Vector();
        TextOutlineColor = KnownColor.Black.Vector();
        FontSize = 12;
        FontType = FontType.Axis;
    }

    protected override void Dispose(bool disposing) {
        if (disposing) {
            stringBuffer->Dtor(true);
        }
        
        base.Dispose(disposing);
    }

    public Vector4 TextColor {
        get => InternalNode->TextColor.ToVector4();
        set => InternalNode->TextColor = value.ToByteColor();
    }

    public Vector4 TextOutlineColor {
        get => InternalNode->EdgeColor.ToVector4();
        set => InternalNode->EdgeColor = value.ToByteColor();
    }

    public Vector4 BackgroundColor {
        get => InternalNode->BackgroundColor.ToVector4();
        set => InternalNode->BackgroundColor = value.ToByteColor();
    }

    public uint SelectStart {
        get => InternalNode->SelectStart;
        set => InternalNode->SelectStart = value;
    }

    public uint SelectEnd {
        get => InternalNode->SelectEnd;
        set => InternalNode->SelectEnd = value;
    }

    public AlignmentType AlignmentType {
        get => InternalNode->AlignmentType;
        set => InternalNode->AlignmentType = value;
    }

    public FontType FontType {
        get => InternalNode->FontType;
        set => InternalNode->FontType = value;
    }

    public TextFlags TextFlags {
        get => (TextFlags) InternalNode->TextFlags;
        set => InternalNode->TextFlags = (byte) value;
    }

    public TextFlags2 TextFlags2 {
        get => (TextFlags2) InternalNode->TextFlags2;
        set => InternalNode->TextFlags2 = (byte) value;
    }

    public uint FontSize {
        get => InternalNode->FontSize;
        set => InternalNode->FontSize = (byte) value;
    }

    public uint LineSpacing {
        get => InternalNode->LineSpacing;
        set => InternalNode->LineSpacing = (byte) value;
    }
    
    public uint CharSpacing {
        get => InternalNode->CharSpacing;
        set => InternalNode->CharSpacing = (byte) value;
    }

    public uint TextId {
        get => InternalNode->TextId;
        set => InternalNode->TextId = value;
    }

    public void SetNumber(int number, bool showCommas = false, bool showPlusSign = false, int digits = 0, bool zeroPad = false)
        => InternalNode->SetNumber(number, showCommas, showPlusSign, (byte) digits, zeroPad);

    /// <summary>
    /// If you want the node to resize automatically, use TextFlags.AutoAdjustNodeSize <b><em>before</em></b> setting the Text property.
    /// </summary>
    public SeString Text {
        get => InternalNode->GetText().AsDalamudSeString();
        set {
            stringBuffer->SetString(value.EncodeWithNullTerminator());
            if (stringBuffer->StringPtr.Value is not null) {
                InternalNode->SetText(stringBuffer->StringPtr);
            }
        }
    }

    public void SetStyle(TextNodeStyle style) {
        SetStyle(style as NodeBaseStyle);

        TextColor = style.TextColor;
        TextOutlineColor = style.TextOutlineColor;
        BackgroundColor = style.BackgroundColor;
        AlignmentType = style.AlignmentType;
        FontType = style.FontType;
        TextFlags = style.TextFlags;
        TextFlags2 = style.TextFlags2;
        FontSize = (uint) style.FontSize;
        LineSpacing = (uint) style.LineSpacing;
        CharSpacing = (uint) style.CharacterSpacing;
    }
}
