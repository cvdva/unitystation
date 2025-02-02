
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using IngameDebugConsole;
using UnityEngine;
using Mirror;
using UnityEngine.Serialization;
using Random = System.Random;

/// <summary>
/// Various attributes associated with a particular item.
/// New and improved, removes need for UniCloth type stuff, works
/// well with using prefab variants.
/// </summary>
[RequireComponent(typeof(Pickupable))] //Inventory interaction
[RequireComponent(typeof(ObjectBehaviour))] //pull and Push
[RequireComponent(typeof(RegisterItem))] //Registry with subsistence
public class ItemAttributesV2 : Attributes
{
	[SerializeField]
	[Tooltip("Initial traits of this item on spawn.")]
	private List<ItemTrait> initialTraits;

	[Tooltip("Size of this item when spawned.")]
	[SerializeField]
	private ItemSize initialSize;

	/// <summary>
	/// Current size.
	/// </summary>
	[SyncVar(hook=nameof(SyncSize))]
	private ItemSize size;
	/// <summary>
	/// Current size
	/// </summary>
	public ItemSize Size => size;

	[Tooltip("Damage when we click someone with harm intent")]
	[Range(0, 100)]
	[SerializeField]
	private float hitDamage = 0;
	/// <summary>
	/// Damage when we click someone with harm intent, tracked server side only.
	/// </summary>
	public float ServerHitDamage
	{
		get => hitDamage;
		set => hitDamage = value;
	}

	[Tooltip("Type of damage done when this is thrown or used for melee.")]
	[SerializeField]
	private DamageType damageType = DamageType.Brute;
	/// <summary>
	/// Type of damage done when this is thrown or used for melee, tracked server side only.
	/// </summary>
	public DamageType ServerDamageType
	{
		get => damageType;
		set => damageType = value;
	}

	[Tooltip("How painful it is when someone throws it at you")]
	[Range(0, 100)]
	[SerializeField]
	private float throwDamage = 0;
	/// <summary>
	/// Amout of damage done when this is thrown, tracked server side only.
	/// </summary>
	public float ServerThrowDamage
	{
		get => throwDamage;
		set => throwDamage = value;
	}

	[Tooltip("How many tiles to move per 0.1s when being thrown")]
	[SerializeField]
	private float throwSpeed = 2;
	/// <summary>
	/// How many tiles to move per 0.1s when being thrown
	/// </summary>
	public float ThrowSpeed => throwSpeed;

	[Tooltip("Max throw distance")]
	[SerializeField]
	private float throwRange = 7;
	/// <summary>
	/// Max throw distance
	/// </summary>
	public float ThrowRange => throwRange;

	[Tooltip("Sound to be played when we click someone with harm intent")]
	[SerializeField]
	private string hitSound = "GenericHit";
	/// <summary>
	/// Sound to be played when we click someone with harm intent, tracked server side only
	/// </summary>
	public string ServerHitSound
	{
		get => hitSound;
		set => hitSound = value;
	}

	//TODO: tank / eva fields should probably be migrated to a different component as they are very specific to clothing, particularly
	//suits and masks. Probably belong in the Clothing component.
	[Tooltip("Is this a mask that can connect to a tank?")]
	[SerializeField]
	private bool canConnectToTank;
	/// <summary>
	/// Whether this item can connect to a gas tank.
	/// </summary>
	public bool CanConnectToTank => canConnectToTank;


	[Tooltip("Can this item protect humans against spess?")]
	[SerializeField]
	private bool isEVACapable;
	/// <summary>
	/// Can this item protect humans against spess?
	/// </summary>
	public bool IsEVACapable => isEVACapable;

	[Tooltip("Possible verbs used to describe the attack when this is used for melee.")]
	[SerializeField]
	private List<string> attackVerbs;
	/// <summary>
	/// Possible verbs used to describe the attack when this is used for melee, tracked server side only.
	/// </summary>
	public IEnumerable<string> ServerAttackVerbs
	{
		get => attackVerbs;
		set => attackVerbs = new List<string>(value);
	}

	/// <summary>
	/// Actual current traits, accounting for dynamic add / remove. Note that these adds / removes
	/// are not currently synced between client / server.
	/// </summary>
	private HashSet<ItemTrait> traits = new HashSet<ItemTrait>();

	/// <summary>
	/// SpriteDataHandler on this object
	/// </summary>
	public SpriteDataHandler SpriteDataHandler => spriteDataHandler;
	private SpriteDataHandler spriteDataHandler;

	private void Awake()
	{
		foreach (var definedTrait in initialTraits)
		{
			traits.Add(definedTrait);
		}
		spriteDataHandler = GetComponentInChildren<SpriteDataHandler>();
	}


	public override void OnStartClient()
	{
		SyncSize(this.size);
		base.OnStartClient();
	}

	public override void OnSpawnServer(SpawnInfo info)
	{
		SyncSize(initialSize);
		base.OnSpawnServer(info);
	}

	/// <summary>
	/// All traits currently on the item.
	/// NOTE: Dynamically added / removed traits are not synced between client / server
	/// </summary>
	/// <returns></returns>
	public IEnumerable<ItemTrait> GetTraits()
	{
		return traits;
	}

	/// <summary>
	/// Does it have the given trait?
	/// NOTE: Dynamically added / removed traits are not synced between client / server
	/// </summary>
	/// <param name="itemTrait"></param>
	public bool HasTrait(ItemTrait toCheck)
	{
		return traits.Contains(toCheck);
	}


	/// <summary>
	/// Does it have any of the given traits?
	/// </summary>
	/// <param name="expectedTraits"></param>
	/// <returns></returns>
	public bool HasAnyTrait(IEnumerable<ItemTrait> expectedTraits)
	{
		return traits.Any(expectedTraits.Contains);
	}

	/// <summary>
	/// Does it have all of the given traits?
	/// </summary>
	/// <param name="expectedTraits"></param>
	/// <returns></returns>
	public bool HasAllTraits(IEnumerable<ItemTrait> expectedTraits)
	{
		return traits.All(expectedTraits.Contains);
	}

	/// <summary>
	/// Adds the trait dynamically
	/// NOTE: Not synced between client / server
	/// </summary>
	/// <param name="itemTrait"></param>
	public void AddTrait(ItemTrait toAdd)
	{
		traits.Add(toAdd);
	}

	private void SyncSize(ItemSize newSize)
	{
		size = newSize;
	}

	/// <summary>
	/// Removes the trait dynamically
	/// NOTE: Not synced between client / server
	/// </summary>
	/// <param name="itemTrait"></param>
	public void RemoveTrait(ItemTrait toRemove)
	{
		traits.Remove(toRemove);
	}

	private static string GetMasterTypeHandsString(SpriteType masterType)
	{
		switch (masterType)
		{
			case SpriteType.Clothing: return "clothing";

			default: return "items";
		}
	}
	public string ItemName => name;

	/// <summary>
	/// CHange this item's size and sync it to clients
	/// </summary>
	/// <param name="newSize"></param>
	[Server]
	public void ServerSetSize(ItemSize newSize)
	{
		SyncSize(newSize);
	}
}