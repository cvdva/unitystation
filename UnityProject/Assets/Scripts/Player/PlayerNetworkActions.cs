﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using Mirror;

public partial class PlayerNetworkActions : NetworkBehaviour
{
	// For access checking. Must be nonserialized.
	// This has to be added because using the UIManager at client gets the server's UIManager. So instead I just had it send the active hand to be cached at server.
	[NonSerialized] public NamedSlot activeHand = NamedSlot.rightHand;

	private PlayerChatBubble playerChatBubble;

	private Equipment equipment;

	private PlayerMove playerMove;
	private PlayerScript playerScript;
	private ItemStorage itemStorage;

	private void Awake()
	{
		playerMove = GetComponent<PlayerMove>();
		playerScript = GetComponent<PlayerScript>();
		playerChatBubble = GetComponentInChildren<PlayerChatBubble>();
		itemStorage = GetComponent<ItemStorage>();
	}

	/// <summary>
	/// Get the item in the player's active hand
	/// </summary>
	/// <returns>the gameobject item in the player's active hand, null if nothing in active hand</returns>
	public GameObject GetActiveHandItem()
	{
		var pu = itemStorage.GetNamedItemSlot(activeHand).Item;
		return pu != null ? pu.gameObject : null;
	}

	/// Checks if player has this item in any of his slots
	[Server]
	public bool HasItem(GameObject item)
	{
		foreach (var slot in itemStorage.GetItemSlotTree())
		{
			if (item == slot.Item?.gameObject)
			{
				return true;
			}
		}

		return false;
	}

	private bool IsEquipSpriteSlot(ItemSlot slot)
	{
		return slot.SlotIdentifier.NamedSlot != null;
	}


	[Server]
	private void SyncEquipSprite(string slotName, GameObject Item)
	{
		NamedSlot enumA = (NamedSlot) Enum.Parse(typeof(NamedSlot), slotName);
		equipment.SetReference((int) enumA, Item);
	}

	/// <summary>
	/// Server handling of the request to drop an item from a client
	/// </summary>
	[Command]
	public void CmdDropItem(NamedSlot equipSlot)
	{
		//only allowed to drop from hands
		if (equipSlot != NamedSlot.leftHand && equipSlot != NamedSlot.rightHand) return;

		//allowed to drop from hands while cuffed
		if (!Validations.CanInteract(playerScript, NetworkSide.Server, allowCuffed: true)) return;

		var slot = itemStorage.GetNamedItemSlot(equipSlot);
		Inventory.ServerDrop(slot);
	}

	/// <summary>
	/// Completely disrobes another player
	/// </summary>
	[Command]
	public void CmdDisrobe(GameObject toDisrobe)
	{
		if (!Validations.CanApply(playerScript, toDisrobe, NetworkSide.Server)) return;
		//only allowed if this player is an observer of the player to disrobe
		var itemStorage = toDisrobe.GetComponent<ItemStorage>();
		if (itemStorage == null) return;

		//are we an observer of the player to disrobe?
		if (!itemStorage.ServerIsObserver(gameObject)) return;

		//disrobe each slot, taking .2s per each occupied slot
		//calculate time
		var occupiedSlots = itemStorage.GetItemSlots().Count(slot => slot.NamedSlot != NamedSlot.handcuffs && !slot.IsEmpty);
		if (occupiedSlots == 0) return;
		var timeTaken = occupiedSlots * .4f;
		var finishProgressAction = new ProgressCompleteAction(() =>
		{
			foreach (var itemSlot in itemStorage.GetItemSlots())
			{
				//skip slots which have special uses
				if (itemSlot.NamedSlot == NamedSlot.handcuffs) continue;
				Inventory.ServerDrop(itemSlot);
			}
		});
		UIManager.ServerStartProgress(ProgressAction.Disrobe, toDisrobe.transform.position, timeTaken,
			finishProgressAction, gameObject);
	}

	/// <summary>
	/// Server handling of the request to throw an item from a client
	/// </summary>
	[Command]
	public void CmdThrow(NamedSlot equipSlot, Vector3 worldTargetPos, int aim)
	{
		//only allowed to throw from hands
		if (equipSlot != NamedSlot.leftHand && equipSlot != NamedSlot.rightHand) return;
		if (!Validations.CanInteract(playerScript, NetworkSide.Server)) return;

		var slot = itemStorage.GetNamedItemSlot(equipSlot);
		Inventory.ServerThrow(slot, worldTargetPos,
			equipSlot == NamedSlot.leftHand ? SpinMode.Clockwise : SpinMode.CounterClockwise, (BodyPartType) aim);
	}

	[Command] //Remember with the parent you can only send networked objects:
	public void CmdPlaceItem(NamedSlot equipSlot, Vector3 worldPos, GameObject newParent, bool isTileMap)
	{
		var targetVector = worldPos - gameObject.TileWorldPosition().To3Int();
		if (!Validations.CanApply(playerScript, newParent, NetworkSide.Server, targetVector: targetVector)) return;

		var slot = itemStorage.GetNamedItemSlot(equipSlot);
		Inventory.ServerDrop(slot, worldPos);
	}

	[Command]
	public void CmdToggleShutters(GameObject switchObj)
	{
		if (!Validations.CanApply(playerScript, switchObj, NetworkSide.Server)) return;

		if (CanInteractWallmount(switchObj.GetComponent<WallmountBehavior>()))
		{
			ShutterSwitch s = switchObj.GetComponent<ShutterSwitch>();
			if (s.IsClosed)
			{
				s.IsClosed = false;
			}
			else
			{
				s.IsClosed = true;
			}
		}
		else
		{
			Logger.LogWarningFormat("Player {0} attempted to interact with shutter switch through wall," +
			                        " this could indicate a hacked client.", Category.Exploits, this.gameObject.name);
		}
	}

	[Command]
	public void CmdToggleLightSwitch(GameObject switchObj)
	{
		if (!Validations.CanApply(playerScript, switchObj, NetworkSide.Server)) return;
		if (CanInteractWallmount(switchObj.GetComponent<WallmountBehavior>()))
		{
			LightSwitch s = switchObj.GetComponent<LightSwitch>();
			if (s.isOn == LightSwitch.States.On)
			{
				s.isOn = LightSwitch.States.Off;
			}
			else if (s.isOn == LightSwitch.States.Off)
			{
				s.isOn = LightSwitch.States.On;
			}
		}
		else
		{
			Logger.LogWarningFormat("Player {0} attempted to interact with light switch through wall," +
			                        " this could indicate a hacked client.", Category.Exploits, this.gameObject.name);
		}
	}

	/// <summary>
	/// Validates that the player can interact with the specified wallmount
	/// </summary>
	/// <param name="wallmount">wallmount to check</param>
	/// <returns>true iff interaction is allowed</returns>
	[Server]
	private bool CanInteractWallmount(WallmountBehavior wallmount)
	{
		//can only interact if the player is facing the wallmount
		return wallmount.IsFacingPosition(transform.position);
	}

	/// <summary>
	/// Everything that needs to be done when reentering body.
	/// </summary>
	/// <param name="recipient">The player to be synced.</param>
	[Server]
	public void ReenterBodyUpdates()
	{
		UpdateInventorySlots();
	}

	/// <summary>
	/// Make client a listener of each slot
	/// </summary>
	[Server]
	private void UpdateInventorySlots()
	{
		var body = playerScript.mind.body.gameObject;
		//player gets inventory slot updates again
		foreach (var slot in itemStorage.GetItemSlotTree())
		{
			slot.ServerAddObserverPlayer(body);
		}
	}

	/// <summary>
	/// Process the effects of a conscious state being changed (invoked from PlayerHealth on server when
	/// conscious state changes)
	/// </summary>
	/// <param name="oldState"></param>
	/// <param name="newState"></param>
	[Server]
	public void OnConsciousStateChanged(ConsciousState oldState, ConsciousState newState)
	{
		playerScript.registerTile.IsDownServer = newState != ConsciousState.CONSCIOUS;
		switch (newState)
		{
			case ConsciousState.CONSCIOUS:
				playerMove.allowInput = true;
				playerScript.PlayerSync.SpeedServer = playerMove.RunSpeed;
				break;
			case ConsciousState.BARELY_CONSCIOUS:
				//Drop hand items when unconscious
				Inventory.ServerDrop(itemStorage.GetNamedItemSlot(NamedSlot.leftHand));
				Inventory.ServerDrop(itemStorage.GetNamedItemSlot(NamedSlot.rightHand));
				playerMove.allowInput = true;
				playerScript.PlayerSync.SpeedServer = playerMove.CrawlSpeed;
				if (oldState == ConsciousState.CONSCIOUS)
				{
					//only play the sound if we are falling
					SoundManager.PlayNetworkedAtPos("Bodyfall", transform.position);
				}

				break;
			case ConsciousState.UNCONSCIOUS:
				//Drop items when unconscious
				Inventory.ServerDrop(itemStorage.GetNamedItemSlot(NamedSlot.leftHand));
				Inventory.ServerDrop(itemStorage.GetNamedItemSlot(NamedSlot.rightHand));
				playerMove.allowInput = false;
				if (oldState == ConsciousState.CONSCIOUS)
				{
					//only play the sound if we are falling
					SoundManager.PlayNetworkedAtPos("Bodyfall", transform.position);
				}

				break;
		}

		playerScript.pushPull.CmdStopPulling();
	}

	[Command]
	public void CmdToggleChatIcon(bool turnOn, string message, ChatChannel chatChannel)
	{
		if (!playerScript.pushPull.VisibleState || (playerScript.mind.occupation.JobType == JobType.NULL)
		                                        || playerScript.playerHealth.IsDead || playerScript.playerHealth.IsCrit
		                                        || playerScript.playerHealth.IsCardiacArrest)
		{
			//Don't do anything with chat icon if player is invisible or not spawned in
			//This will also prevent clients from snooping other players local chat messages that aren't visible to them
			return;
		}

		RpcToggleChatIcon(turnOn, message, chatChannel);
	}

	[ClientRpc]
	private void RpcToggleChatIcon(bool turnOn, string message, ChatChannel chatChannel)
	{
		if (!playerChatBubble)
		{
			playerChatBubble = GetComponentInChildren<PlayerChatBubble>();
		}

		playerChatBubble.DetermineChatVisual(turnOn, message, chatChannel);
	}

	[Command]
	public void CmdCommitSuicide()
	{
		GetComponent<LivingHealthBehaviour>().ApplyDamage(gameObject, 1000, AttackType.Internal, DamageType.Brute);
	}

	//Respawn action for Deathmatch v 0.1.3

	[Command]
	public void CmdRespawnPlayer()
	{
		if (GameManager.Instance.RespawnCurrentlyAllowed)
		{
			PlayerSpawn.ServerRespawnPlayer(playerScript.mind);
		}
	}

	[Command]
	public void CmdToggleAllowCloning()
	{
		playerScript.mind.DenyCloning = !playerScript.mind.DenyCloning;
	}

	/// <summary>
	/// Spawn the ghost for this player and tell the client to switch input / camera to it
	/// </summary>
	[Command]
	public void CmdSpawnPlayerGhost()
	{
		if (GetComponent<LivingHealthBehaviour>().IsDead)
		{
			PlayerSpawn.ServerSpawnGhost(playerScript.mind);
		}
	}


	/// <summary>
	/// Asks the server to let the client rejoin into a logged off character.
	/// </summary>
	[Command]
	public void CmdGhostEnterBody()
	{
		PlayerScript body = playerScript.mind.body;
		if ( !playerScript.IsGhost || !body.playerHealth.IsDead )
		{
			Logger.LogWarningFormat( "Either player {0} is not dead or not currently a ghost, ignoring EnterBody", Category.Health, body );
			return;
		}
		if ( body.WorldPos == TransformState.HiddenPos )
		{
			Logger.LogFormat( "There's nothing left of {0}'s body, not entering it", Category.Health, body );
			return;
		}
		playerScript.mind.StopGhosting();
		PlayerSpawn.ServerGhostReenterBody(connectionToClient, gameObject, playerScript.mind);
	}

	/// <summary>
	/// Disables input before a body transfer.
	/// Note this will be invoked on all clients.
	/// </summary>
	[ClientRpc]
	public void RpcBeforeBodyTransfer()
	{
		//no more input can be sent to the body.
		GetComponent<MouseInputController>().enabled = false;
	}

	//FOOD
	[Command]
	public void CmdEatFood(GameObject food, NamedSlot fromSlot, bool isDrink)
	{
		if (!Validations.CanInteract(playerScript, NetworkSide.Server)) return;

		var slot = itemStorage.GetNamedItemSlot(fromSlot);
		if (slot.Item == null)
		{
			//Already been eaten or the food is no longer in hand
			return;
		}

		Edible baseFood = food.GetComponent<Edible>();
		if (isDrink)
		{
			SoundManager.PlayNetworkedAtPos("Slurp", transform.position);
		}
		else
		{
			SoundManager.PlayNetworkedAtPos("EatFood", transform.position);
		}

		PlayerHealth playerHealth = GetComponent<PlayerHealth>();

		//FIXME: remove blood changes after TDM
		//and use this Cmd for healing hunger and applying
		//food related attributes instead:
		playerHealth.bloodSystem.BloodLevel += baseFood.healAmount;
		playerHealth.bloodSystem.StopBleedingAll();

		Inventory.ServerDespawn(slot);

		GameObject leavings = baseFood.leavings;
		if (leavings != null)
		{
			leavings = Spawn.ServerPrefab(leavings).GameObject;
			Inventory.ServerAdd(leavings.GetComponent<Pickupable>(), slot);
		}
	}

	[Command]
	public void CmdSetActiveHand(NamedSlot hand)
	{
		activeHand = hand;
	}

	[Command]
	public void CmdRequestPaperEdit(GameObject paper, string newMsg)
	{
		if (!Validations.CanInteract(playerScript, NetworkSide.Server)) return;

		//Validate paper edit request
		//TODO Check for Pen
		var leftHand = itemStorage.GetNamedItemSlot(NamedSlot.leftHand);
		var rightHand = itemStorage.GetNamedItemSlot(NamedSlot.rightHand);
		if (leftHand.Item?.gameObject == paper || rightHand.Item?.gameObject == paper)
		{
			var paperComponent = paper.GetComponent<Paper>();
			var pen = leftHand.Item?.GetComponent<Pen>();
			if (pen == null)
			{
				pen = rightHand.Item?.GetComponent<Pen>();
				if (pen == null)
				{
					//no pen
					paperComponent.UpdatePlayer(gameObject); //force server string to player
					return;
				}
			}

			if (paperComponent != null)
			{
				paperComponent.SetServerString(newMsg);
				paperComponent.UpdatePlayer(gameObject);
			}
		}
	}

	[Command]
	public void CmdRequestRename(GameObject target, string customName)
	{
		var rename = target.GetComponent<Renameable>();

		if (rename == null)
		{
			return;
		}

		if (customName.Length > 42)
		{
			customName = customName.Substring(0, 42);
		}

		customName = Regex.Replace(customName, "<size=\"(.*)\">", "", RegexOptions.IgnoreCase);
		customName = customName.Replace("</size>", "");

		rename.SetCustomName(customName);
	}

	/// <summary>
	/// Performs a hug from one player to another.
	/// </summary>
	[Command]
	public void CmdRequestHug(string hugger, GameObject huggedPlayer)
	{
		//validate that hug can be done
		if (!Validations.CanApply(playerScript, huggedPlayer, NetworkSide.Server)) return;

		string hugged = huggedPlayer.GetComponent<PlayerScript>().playerName;
		var lhb = gameObject.GetComponent<LivingHealthBehaviour>();
		var lhbOther = huggedPlayer.GetComponent<LivingHealthBehaviour>();
		if (lhb != null && lhbOther != null && (lhb.FireStacks > 0 || lhbOther.FireStacks > 0))
		{
			lhb.ApplyDamage(huggedPlayer, 1, AttackType.Fire, DamageType.Burn);
			lhbOther.ApplyDamage(gameObject, 1, AttackType.Fire, DamageType.Burn);
			Chat.AddCombatMsgToChat(gameObject, $"You give {hugged} a fiery hug.", $"{hugger} has given {hugged} a fiery hug.");
		}
		else
		{
			Chat.AddActionMsgToChat(gameObject, $"You hugged {hugged}.", $"{hugger} has hugged {hugged}.");
		}
	}

	/// <summary>
	///	Performs a CPR action from one player to another.
	/// </summary>
	[Command]
	public void CmdRequestCPR(GameObject cardiacArrestPlayer)
	{
		//TODO: Probably refactor this to IF2
		if (!Validations.CanApply(playerScript, cardiacArrestPlayer, NetworkSide.Server)) return;

		var cardiacArrestPlayerRegister = cardiacArrestPlayer.GetComponent<RegisterPlayer>();

		var progressFinishAction = new ProgressCompleteAction(() => DoCPR(playerScript.gameObject, cardiacArrestPlayer));

		var bar = UIManager.ServerStartProgress(ProgressAction.CPR, cardiacArrestPlayerRegister.WorldPosition, 5f, progressFinishAction,
			playerScript.gameObject);

		if (bar != null)
		{
			Chat.AddActionMsgToChat(playerScript.gameObject, $"You begin performing CPR on {cardiacArrestPlayer.Player()?.Name}.",
			$"{playerScript.gameObject.Player()?.Name} is trying to perform CPR on {cardiacArrestPlayer.Player()?.Name}.");
		}
	}

	[Server]
	private void DoCPR(GameObject rescuer, GameObject CardiacArrestPlayer)
	{
		CardiacArrestPlayer.GetComponent<PlayerHealth>().bloodSystem.oxygenDamage -= 7f;

		Chat.AddActionMsgToChat(rescuer, $"You have performed CPR on {CardiacArrestPlayer.Player()?.Name}.",
			$"{rescuer.Player()?.Name} has performed CPR on {CardiacArrestPlayer.Player()?.Name}.");
	}

	/// <summary>
	/// Performs a disarm attempt from one player to another.
	/// </summary>
	[Command]
	public void CmdRequestDisarm(GameObject playerToDisarm)
	{
		if (!Validations.CanApply(playerScript, playerToDisarm, NetworkSide.Server)) return;
		var rng = new System.Random();
		string disarmerName = playerScript.gameObject.Player()?.Name;
		string playerToDisarmName = playerToDisarm.Player()?.Name;
		var disarmStorage = playerToDisarm.GetComponent<ItemStorage>();
		var leftHandSlot = disarmStorage.GetNamedItemSlot(NamedSlot.leftHand);
		var rightHandSlot = disarmStorage.GetNamedItemSlot(NamedSlot.rightHand);
		var disarmedPlayerRegister = playerToDisarm.GetComponent<RegisterPlayer>();
		var disarmedPlayerNetworkActions = playerToDisarm.GetComponent<PlayerNetworkActions>();

		// This is based off the alien/humanoid/attack_hand disarm code of TGStation's codebase.
		// Disarms have 5% chance to knock down, then it has a 50% chance to disarm.
		if (5 >= rng.Next(1, 100))
		{
			disarmedPlayerRegister.Stun(6f, false);
			SoundManager.PlayNetworkedAtPos("ThudSwoosh", disarmedPlayerRegister.WorldPositionServer);

			Chat.AddCombatMsgToChat(gameObject, $"You have knocked {playerToDisarmName} down!",
				$"{disarmerName} has knocked {playerToDisarmName} down!");
		}

		else if (50 >= rng.Next(1, 100))
		{
			// Disarms
			if (leftHandSlot.Item != null)
			{
				Inventory.ServerDrop(leftHandSlot);
			}

			if (rightHandSlot.Item != null)
			{
				Inventory.ServerDrop(rightHandSlot);
			}

			SoundManager.PlayNetworkedAtPos("ThudSwoosh", disarmedPlayerRegister.WorldPositionServer);

			Chat.AddCombatMsgToChat(gameObject, $"You have disarmed {playerToDisarmName}!",
				$"{disarmerName} has disarmed {playerToDisarmName}!");
		}
		else
		{
			SoundManager.PlayNetworkedAtPos("PunchMiss", disarmedPlayerRegister.WorldPositionServer);

			Chat.AddCombatMsgToChat(gameObject, $"You attempted to disarm {playerToDisarmName}!",
				$"{disarmerName} has attempted to disarm {playerToDisarmName}!");
		}
	}

	//admin only commands

	#region Admin

	[Command]
	public void CmdAdminMakeHotspot(GameObject onObject)
	{
		var reactionManager = onObject.GetComponentInParent<ReactionManager>();
		reactionManager.ExposeHotspotWorldPosition(onObject.TileWorldPosition(), 700, .05f);
		reactionManager.ExposeHotspotWorldPosition(onObject.TileWorldPosition() + Vector2Int.down, 700, .05f);
		reactionManager.ExposeHotspotWorldPosition(onObject.TileWorldPosition() + Vector2Int.left, 700, .05f);
		reactionManager.ExposeHotspotWorldPosition(onObject.TileWorldPosition() + Vector2Int.up, 700, .05f);
		reactionManager.ExposeHotspotWorldPosition(onObject.TileWorldPosition() + Vector2Int.right, 700, .05f);
	}

	[Command]
	public void CmdAdminSmash(GameObject toSmash)
	{
		if ( toSmash == null )
		{
			return;
		}

		var integrity = toSmash.GetComponent<Integrity>();
		if ( integrity == null )
		{
			return;
		}
		integrity.ApplyDamage(float.MaxValue, AttackType.Melee, DamageType.Brute);
	}

	//simulates despawning and immediately respawning this object, expectation
	//being that it should properly initialize itself regardless of its previous state.
	[Command]
	public void CmdAdminRespawn(GameObject toRespawn)
	{
		Spawn.ServerPoolTestRespawn(toRespawn);
	}

	#endregion
}