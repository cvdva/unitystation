
using System;
using NUnit.Framework.Internal;
using UnityEngine;
using Random = UnityEngine.Random;

/// <summary>
/// Changes the tile to a different tile when an item with a particular
/// trait is used on the tile. Uses progress actions, so also supports specifying a time
/// and messages.
/// </summary>
[CreateAssetMenu(fileName = "ChangeTileWhenItemUsed", menuName = "Interaction/TileInteraction/ChangeTileWhenItemUsed")]
public class ChangeTileWhenItemUsed : TileInteraction
{
	[Tooltip("Trait required on the used item in order to deconstruct the tile. If welder, checks if it's on.")]
	[SerializeField]
	private ItemTrait requiredTrait;

	[Tooltip("Action message to performer when they begin this interaction.")]
	[SerializeField]
	private string performerStartActionMessage;

	[Tooltip("Use {performer} for performer name. Action message to others when the performer begins this interaction.")]
	[SerializeField]
	private string othersStartActionMessage;

	[Tooltip("Seconds taken to perform this action. Leave at 0 for instant.")]
	[SerializeField]
	private float seconds;

	[Tooltip("Tile to change to on completion.")]
	[SerializeField]
	private LayerTile toTile;

	[Tooltip("Action message to performer when they finish this interaction.")]
	[SerializeField]
	private string performerFinishActionMessage;

	[Tooltip("Use {performer} for performer name. Action message to others when performer finishes this interaction.")]
	[SerializeField]
	private string othersFinishActionMessage;

	public override bool WillInteract(TileApply interaction, NetworkSide side)
	{
		if (!DefaultWillInteract.Default(interaction, side)) return false;

		if (requiredTrait == CommonTraits.Instance.Welder)
		{
			var welder = interaction.HandObject.GetComponent<Welder>();
			if (welder == null) return false;
			return welder.isOn;
		}
		return Validations.HasItemTrait(interaction.HandObject, requiredTrait);
	}

	public override void ServerPerformInteraction(TileApply interaction)
	{
		Chat.AddActionMsgToChat(interaction.Performer, performerStartActionMessage,
			Chat.ReplacePerformer(othersStartActionMessage, interaction.Performer));
		var progressFinishAction = new ProgressCompleteAction(() =>
		{
			Chat.AddActionMsgToChat(interaction.Performer, performerFinishActionMessage,
				Chat.ReplacePerformer(othersFinishActionMessage, interaction.Performer));

			interaction.TileChangeManager.UpdateTile(interaction.TargetCellPos, toTile);
			interaction.TileChangeManager.SubsystemManager.UpdateAt(interaction.TargetCellPos);
		});
		ToolUtils.ServerUseTool(interaction, seconds, progressFinishAction);
	}
}
