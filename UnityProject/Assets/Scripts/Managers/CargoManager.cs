﻿using System.Collections;
using System.Collections.Generic;
using Lucene.Net.Support;
using UnityEngine;
using UnityEngine.Events;

public class CargoManager : MonoBehaviour
{
	private static CargoManager _cargoManager;
	public static CargoManager Instance
	{
		get
		{
			if (_cargoManager == null)
			{
				_cargoManager = FindObjectOfType<CargoManager>();
			}
			return _cargoManager;
		}
	}

	public int Credits;
	public ShuttleStatus ShuttleStatus = ShuttleStatus.DockedStation;
	public float CurrentFlyTime = 0f;
	public string CentcomMessage = "";	// Message that will appear in status tab. Resets on sending shuttle to centcom.
	public List<CargoOrderCategory> Supplies = new List<CargoOrderCategory>(); // Supplies - all the stuff cargo can order
	public CargoOrderCategory CurrentCategory ;
	public List<CargoOrder> CurrentOrders = new List<CargoOrder>(); // Orders - payed orders that will spawn in shuttle on centcom arrival
	public List<CargoOrder> CurrentCart = new List<CargoOrder>(); // Cart - current orders, that haven't been payed for/ordered yet

	public CargoUpdateEvent OnCartUpdate = new CargoUpdateEvent();
	public CargoUpdateEvent OnShuttleUpdate = new CargoUpdateEvent();
	public CargoUpdateEvent OnCreditsUpdate = new CargoUpdateEvent();
	public CargoUpdateEvent OnCategoryUpdate = new CargoUpdateEvent();
	public CargoUpdateEvent OnTimerUpdate = new CargoUpdateEvent();

	[SerializeField]
	private CargoData cargoData;

	[SerializeField]
	private float shuttleFlyDuration = 10f;

	private HashMap<string, ExportedItem> exportedItems = new HashMap<string, ExportedItem>();

	/// <summary>
	/// Calls the shuttle.
	/// Server only.
	/// </summary>
	public void CallShuttle()
	{
		if (!CustomNetworkManager.Instance._isServer)
		{
			return;
		}

		if (CurrentFlyTime > 0f)
		{
			return;
		}

		if (CustomNetworkManager.Instance._isServer)
		{
			CurrentFlyTime = shuttleFlyDuration;
			//It works so - shuttle stays in centcomDest until timer is done,
			//then it starts moving to station
			if (ShuttleStatus == ShuttleStatus.DockedCentcom)
			{
				SpawnOrder();
				ShuttleStatus = ShuttleStatus.OnRouteStation;
				CentcomMessage += "Shuttle is sent back with goods." + "\n";
				StartCoroutine(Timer(true));
			}
			//If we are at station - we start timer and launch shuttle at the same time.
			//Once shuttle arrives centcomDest - CargoShuttle will wait till timer is done
			//and will call OnShuttleArrival()
			else if (ShuttleStatus == ShuttleStatus.DockedStation)
			{
				CargoShuttle.Instance.MoveToCentcom();
				ShuttleStatus = ShuttleStatus.OnRouteCentcom;
				CentcomMessage = "";
				exportedItems.Clear();
				StartCoroutine(Timer(false));
			}
		}

		OnShuttleUpdate?.Invoke();
	}

	public void LoadData()
	{
		Supplies = cargoData.Supplies;
	}

	private IEnumerator Timer(bool launchToStation)
	{
		while (CurrentFlyTime > 0f)
		{
			CurrentFlyTime -= 1f;
			OnTimerUpdate?.Invoke();
			yield return WaitFor.Seconds(1);
		}

		if (launchToStation)
		{
			CargoShuttle.Instance.MoveToStation();
		}
	}

	/// <summary>
	/// Method is called once shuttle arrives to its destination.
	/// Server only.
	/// </summary>
	public void OnShuttleArrival()
	{
		if (!CustomNetworkManager.Instance._isServer)
		{
			return;
		}

		if (ShuttleStatus == ShuttleStatus.OnRouteCentcom)
		{

			foreach(var entry in exportedItems)
			{
				CentcomMessage += $"+{ entry.Value.TotalValue } credits: " +
				                  $"{ entry.Value.Count }";

				if (!string.IsNullOrEmpty(entry.Value.ExportMessage) && string.IsNullOrEmpty(entry.Value.ExportName))
				{ // Special handling for items that don't want pluralisation after
					CentcomMessage += $" { entry.Value.ExportMessage }\n";
				}
				else
				{
					CentcomMessage += $" { entry.Key }";

					if (entry.Value.Count > 1)
					{
						CentcomMessage += "s";
					}

					CentcomMessage += $" { entry.Value.ExportMessage }\n";
				}
			}

			ShuttleStatus = ShuttleStatus.DockedCentcom;
		}
		else if (ShuttleStatus == ShuttleStatus.OnRouteStation)
		{
			ShuttleStatus = ShuttleStatus.DockedStation;
		}

		OnShuttleUpdate?.Invoke();
	}

	public void DestroyItem(ObjectBehaviour item)
	{
		// If there is no bounty for the item - we dont destroy it.
		var credits = Instance.GetSellPrice(item);
		if (credits <= 0f)
		{
			return;
		}

		Credits += credits;
		OnCreditsUpdate?.Invoke();

		var attributes = item.gameObject.GetComponent<ItemAttributesV2>();
		string exportName;
		if (attributes)
		{
			if (string.IsNullOrEmpty(attributes.ExportName))
			{
				exportName = attributes.ArticleName;
			}
			else
			{
				exportName = attributes.ExportName;
			}
		}
		else
		{
			exportName = item.gameObject.ExpensiveName();
		}
		ExportedItem export;
		if (exportedItems.ContainsKey(exportName))
		{
			export = exportedItems[exportName];
		}
		else
		{
			export = new ExportedItem
			{
				ExportMessage = attributes ? attributes.ExportMessage : "",
				ExportName = attributes ? attributes.ExportName : "" // Need to always use the given export name
			};
			exportedItems.Add(exportName, export);
		}

		var stackable = item.gameObject.GetComponent<Stackable>();
		var count = 1;
		if (stackable)
		{
			count = stackable.Amount;
		}

		export.Count += count;
		export.TotalValue += credits;

		item.registerTile.UnregisterClient();
		item.registerTile.UnregisterServer();
		Despawn.ServerSingle(item.gameObject);
	}

	private void SpawnOrder()
	{
		CargoShuttle.Instance.PrepareSpawnOrders();
		for (int i = 0; i < CurrentOrders.Count; i++)
		{
			if (CargoShuttle.Instance.SpawnOrder(CurrentOrders[i]))
			{
				CurrentOrders.RemoveAt(i);
				i--;
			}
		}
	}

	public void AddToCart(CargoOrder orderToAdd)
	{
		if (!CustomNetworkManager.Instance._isServer)
		{
			return;
		}

		CurrentCart.Add(orderToAdd);
		OnCartUpdate?.Invoke();
	}

	public void RemoveFromCart(CargoOrder orderToRemove)
	{
		if (!CustomNetworkManager.Instance._isServer)
		{
			return;
		}

		CurrentCart.Remove(orderToRemove);
		OnCartUpdate?.Invoke();
	}

	public void OpenCategory(CargoOrderCategory categoryToOpen)
	{
		if (!CustomNetworkManager.Instance._isServer)
		{
			return;
		}

		CurrentCategory = categoryToOpen;
		OnCategoryUpdate?.Invoke();
	}

	public int TotalCartPrice()
	{
		int totalPrice = 0;
		for (int i = 0; i < CurrentCart.Count; i++)
		{
			totalPrice += CurrentCart[i].CreditsCost;
		}
		return (totalPrice);
	}

	public void ConfirmCart()
	{
		if (!CustomNetworkManager.Instance._isServer)
		{
			return;
		}

		int totalPrice = TotalCartPrice();
		if (totalPrice <= Credits)
		{
			CurrentOrders.AddRange(CurrentCart);
			CurrentCart.Clear();
			Credits -= totalPrice;
		}
		OnCreditsUpdate?.Invoke();
		OnCartUpdate?.Invoke();
		return;
	}

	public int GetSellPrice(ObjectBehaviour item)
	{
		if (!CustomNetworkManager.Instance._isServer)
		{
			return 0;
		}

		var attributes = item.GetComponent<ItemAttributesV2>();

		if (attributes == null)
		{
			return 0;
		}

		return attributes.ExportCost;
	}

	private class ExportedItem
	{
		public string ExportMessage;
		public string ExportName;
		public int Count;
		public int TotalValue;
	}
}

[System.Serializable]
public class CargoOrder
{
	public string OrderName = "Crate with beer and steak";
	public int CreditsCost = 1000;
	public GameObject Crate = null;
	public List<GameObject> Items = new List<GameObject>();
}

[System.Serializable]
public class CargoOrderCategory
{
	public string CategoryName = "";
	public List<CargoOrder> Supplies = new List<CargoOrder>();
}

public class CargoUpdateEvent : UnityEvent {}
