using System;
using System.Collections;
using UnityEngine;
using Mirror;

/// <summary>
/// Main logic for fire cabinet, allows storing an extinguisher.
/// </summary>
[RequireComponent(typeof(ItemStorage))]
public class InteractableFireCabinet : NetworkBehaviour, ICheckedInteractable<HandApply>
{
	[SyncVar(hook = nameof(SyncCabinet))] public bool IsClosed;

	[SyncVar(hook = nameof(SyncItemSprite))] public bool isFull;

	public Sprite spriteClosed;
	public Sprite spriteOpenedEmpty;
	public Sprite spriteOpenedOccupied;
	[SerializeField]
	private SpriteRenderer spriteRenderer;

	private ItemStorage storageObject;
	private ItemSlot slot;

	private void Awake()
	{
		//we have an item storage with only 1 slot.
		//TODO: Populate with fire extinguisher in item storage config
		storageObject = GetComponent<ItemStorage>();
		slot = storageObject.GetIndexedItemSlot(0);
		//TODO: Can probably refactor this component to rely more on ItemStorage and do less of its own logic.
	}

	public override void OnStartServer()
	{
		if (spriteRenderer == null)
		{
			spriteRenderer = transform.Find("Sprite").GetComponent<SpriteRenderer>();
		}
		IsClosed = true;
		isFull = true;
		base.OnStartServer();
	}

	public override void OnStartClient()
	{
		StartCoroutine(WaitForLoad());
		base.OnStartClient();
	}

	private IEnumerator WaitForLoad()
	{
		yield return WaitFor.Seconds(3f);
		SyncCabinet(IsClosed);
		SyncItemSprite(isFull);
	}

	public bool WillInteract(HandApply interaction, NetworkSide side)
	{
		if (!DefaultWillInteract.Default(interaction, side)) return false;

		//only allow interactions targeting this
		if (interaction.TargetObject != gameObject) return false;

		return true;
	}

	public void ServerPerformInteraction(HandApply interaction)
	{
		PlayerNetworkActions pna = interaction.Performer.GetComponent<PlayerNetworkActions>();

		if (IsClosed)
		{
			if(isFull && interaction.HandObject == null) {
				ServerRemoveExtinguisher(interaction.HandSlot);
			}
			IsClosed = false;
		}
		else
		{
			if (isFull)
			{
				if (interaction.HandObject == null)
				{
					ServerRemoveExtinguisher(interaction.HandSlot);
				}
				else
				{
					IsClosed = true;
				}
			}
			else
			{
				if (interaction.HandObject && interaction.HandObject.GetComponent<FireExtinguisher>())
				{
					ServerAddExtinguisher(interaction);
				}
				else
				{
					IsClosed = true;
				}
			}
		}
	}

	private void ServerRemoveExtinguisher(ItemSlot toSlot)
	{
		if (Inventory.ServerTransfer(slot, toSlot))
		{
			isFull = false;
		}
	}

	private void ServerAddExtinguisher(HandApply interaction)
	{
		if (Inventory.ServerTransfer(interaction.HandSlot, slot))
		{
			isFull = true;
		}
	}

	public void SyncItemSprite(bool value)
	{
		isFull = value;
		if (!isFull)
		{
			spriteRenderer.sprite = spriteOpenedEmpty;
		}
		else
		{
			if (!IsClosed)
			{
				spriteRenderer.sprite = spriteOpenedOccupied;
			}
		}
	}

	private void SyncCabinet(bool value)
	{
		IsClosed = value;
		if (IsClosed)
		{
			Close();
		}
		else
		{
			Open();
		}
	}

	private void Open()
	{
		SoundManager.PlayAtPosition("OpenClose", transform.position);
		if (isFull)
		{
			spriteRenderer.sprite = spriteOpenedOccupied;
		}
		else
		{
			spriteRenderer.sprite = spriteOpenedEmpty;
		}
	}

	private void Close()
	{
		SoundManager.PlayAtPosition("OpenClose", transform.position);
		spriteRenderer.sprite = spriteClosed;
	}

}