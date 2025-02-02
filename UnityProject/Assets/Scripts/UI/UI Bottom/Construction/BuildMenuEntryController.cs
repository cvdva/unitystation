using System;
using Construction;
using UnityEngine;
using UnityEngine.UI;

namespace UI.UI_Bottom
{
	/// <summary>
	/// Main logic for an entry in the build menu.
	/// </summary>
	public class BuildMenuEntryController : MonoBehaviour
	{
		[Tooltip("Main image of the object to build")]
		[SerializeField]
		private Image image;

		[Tooltip("Secondary image of the object to build")]
		[SerializeField]
		private Image secondaryImage;

		[Tooltip("Name describing what this entry is.")]
		[SerializeField]
		private Text entryName;

		[Tooltip("Amount of material required for this entry.")]
		[SerializeField]
		private Text materialCost;

		[Tooltip("Image of the material required.")]
		[SerializeField]
		private Image materialImage;

		[Tooltip("Secondary image of the material required.")]
		[SerializeField]
		private Image materialSecondaryImage;

		//menu and entry this entry is for
		private BuildingMaterial buildingMaterial;
		private BuildList.Entry entry;

		private void Awake()
		{
			image.enabled = false;
			secondaryImage.enabled = false;
			materialImage.enabled = false;
			materialSecondaryImage.enabled = false;
		}

		/// <summary>
		/// Initialize this entry using the specified building list enty
		/// </summary>
		/// <param name="entry">entry whose contest should be displayed</param>
		/// <param name="buildingMaterial">buildingMaterial this entry comes from</param>
		public void Initialize(BuildList.Entry entry, BuildingMaterial buildingMaterial)
		{
			this.entry = entry;
			this.buildingMaterial = buildingMaterial;
			image.sprite = null;
			secondaryImage.sprite = null;
			materialImage.sprite = null;
			materialSecondaryImage.sprite = null;
			entryName.text = null;
			materialCost.text = null;

			entry.Prefab.PopulateImageSprites(image, secondaryImage);
			entryName.text = entry.Name;

			buildingMaterial.gameObject.PopulateImageSprites(materialImage, materialSecondaryImage);

			materialCost.text = entry.Cost.ToString();

		}

		public void OnClick()
		{
			RequestConstructionMessage.Send(entry, buildingMaterial);
			UIManager.BuildMenu.CloseBuildMenu();
		}
	}
}