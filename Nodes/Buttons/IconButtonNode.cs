﻿using System.Numerics;

namespace KamiToolKit.Nodes;

/// <summary>
/// Uses a GameIconId to display that icon as the decorator for the button.
/// </summary>
public class IconButtonNode : ButtonBase {

	public readonly IconImageNode ImageNode;
	public readonly NineGridNode BackgroundNode;

	public IconButtonNode() {
		BackgroundNode = new SimpleNineGridNode {
			TexturePath = "ui/uld/BgParts.tex",
			TextureSize = new Vector2(32.0f, 32.0f),
			TextureCoordinates = new Vector2(33.0f, 65.0f),
			TopOffset = 8.0f,
			LeftOffset = 8.0f,
			RightOffset = 8.0f,
			BottomOffset = 8.0f,
			NodeId = 2,
			IsVisible = true,
		};
		BackgroundNode.AttachNode(this);
		
		ImageNode = new IconImageNode {
			IsVisible = true,
			TextureSize = new Vector2(32.0f, 32.0f),
			NodeId = 3,
		};
		ImageNode.AttachNode(this);

		LoadTimelines();
		
		InitializeComponentEvents();
	}
	
	public uint IconId {
		get => ImageNode.IconId;
		set => ImageNode.IconId = value;
	}

	protected override void OnSizeChanged() {
		base.OnSizeChanged();		ImageNode.Size = Size - new Vector2(16.0f, 16.0f);
		ImageNode.Position = BackgroundNode.Position + new Vector2(BackgroundNode.LeftOffset, BackgroundNode.TopOffset);
		BackgroundNode.Size = Size;
	}

	private void LoadTimelines()
		=> LoadThreePartTimelines(this, BackgroundNode, ImageNode, new Vector2(8.0f, 8.0f));
}