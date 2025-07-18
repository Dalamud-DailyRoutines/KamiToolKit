﻿using System;
using System.Collections.Generic;

namespace KamiToolKit.Nodes;

public class TextDropDownNode : DropDownNode<TextListNode, string> {

	public TextDropDownNode() {
		OptionListNode.OnOptionSelected += OptionSelectedHandler;
	}

	public Action<string>? OnOptionSelected { get; set; }
	
	private void OptionSelectedHandler(string option) {
		OnOptionSelected?.Invoke(option);
		UpdateLabel(option);
		Toggle();
	}

	public required List<string>? Options {
		get => OptionListNode.Options;
		set {
			OptionListNode.Options = value;
			OptionListNode.SelectDefaultOption();
			UpdateLabel(OptionListNode.SelectedOption);
		}
	}

	protected override void UpdateLabel(string? option) {
		LabelNode.Text = option ?? "ERROR: Invalid Default Option";
	}
}