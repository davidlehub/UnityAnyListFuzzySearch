using System.Collections;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using System.IO;
using Unity.Plastic.Newtonsoft.Json;

namespace DDuong
{

	/// <summary>
	/// A custom editor window for fuzzy searching through a list of objects.
	/// </summary>
	public class FuzzySearchWindow : EditorWindow {
		public enum SortType {
			Alphabetical,
			ByHistory,
			ByFavorite,
			// Add other sort types here
		}

		[SerializeField]
		protected VisualTreeAsset _uxml;
		[SerializeField]
		private Texture2D favoriteIconOn;
		[SerializeField]
		private Texture2D favoriteIconOff;
		[SerializeField]
		private string DataFolderPath = "Assets/UnityAnyListFuzzySearch/Editor/SavedData";
		private string DataPath = "";


		private const float LevenshteinEmptyStringScore = 0.5f;
		private const int FavoriteIconWidth = 16;
		private const int FavoriteIconHeight = 16;

		private SortType currentSortType = SortType.Alphabetical; // default
		private List<string> _selectionOrder = new();
		private List<string> _favoriteItems = new();

		private Toggle _historySortToggle;
		private Toggle _favoriteSortToggle;
		private List<object> _noFilteredData;
		private List<object> _filteredData;
		private string _prefixToRemove = "";
		private string _filterTextValue = "";
		private MultiColumnListView _listView;
		private TextField _searchInputField;
		private TextField _prefixToRemoveInputField;
		private Func<object, object>[] _fieldExtractors;
		private Action<object> _onItemSelectedCallback;


		[Serializable]
		public class PersistentData {
			public string SerializedFavorites;
			public bool SortByHistory;
			public bool SortByFavorite;
		}

		private string SerializeObject(object obj)
		{
			// Assuming the first column contains a unique string identifier
			return _fieldExtractors[0](obj).ToString();
		}

		private object DeserializeObject(string str)
		{
			// Find the object in the original data based on the unique string identifier
			return _noFilteredData.FirstOrDefault(item => _fieldExtractors[0](item).ToString() == str);
		}

		[Serializable]
		public class SerializedObject {
			public string TypeName;
			public string Data;
			public bool IsFavorite;
		}


		public void OnEnable()
		{
			if (_uxml == null) return;

			DataPath = DataFolderPath + "/" + "FuzzySearchWindowData.json";

			_noFilteredData = new List<object>();

			_uxml.CloneTree(rootVisualElement);

			FindUIElements(); // Initialize UI elements

			LoadPersistentData();
		}

		private void SavePersistentData()
		{
			try
			{
				PersistentData data = new PersistentData {
					SerializedFavorites = JsonConvert.SerializeObject(_favoriteItems),
					SortByHistory = _historySortToggle.value, // Save history sort state
					SortByFavorite = _favoriteSortToggle.value // Save favorite sort state
				};

				string json = JsonConvert.SerializeObject(data, Formatting.Indented);
				EnsureDataFileExists(DataPath);
				File.WriteAllText(DataPath, json);
			}
			catch (Exception e)
			{
				Debug.LogError("An error occurred while saving persistent data: " + e.Message);
			}
		}


		private void LoadPersistentData()
		{
			if (!File.Exists(DataPath))
			{
				return;
			}

			string json = File.ReadAllText(DataPath);
			if (string.IsNullOrEmpty(json))
			{
				Debug.LogError("JSON data is null or empty.");
				return;
			}

			PersistentData data = JsonConvert.DeserializeObject<PersistentData>(json);
			if (data == null)
			{
				Debug.LogError("Deserialized data is null.");
				return;
			}

			_favoriteItems = JsonConvert.DeserializeObject<List<string>>(data.SerializedFavorites);
			_historySortToggle.value = data.SortByHistory; // Restore history sort state
			_favoriteSortToggle.value = data.SortByFavorite; // Restore favorite sort state
		}

		private void OnDisable()
		{
			SavePersistentData();
		}

		/// <summary>
		/// Initializes the GUI elements for the editor window.
		/// </summary>
		void CreateGUI()
		{
			SetupUI();
		}

		public void OnGUI()
		{
			#region-- Pressing alt + another key to toggle favorite or history sorting.
			// Get the current event
			_evtInOnGUI = Event.current;

			// Check if the event is a key down event
			if (_evtInOnGUI.type == EventType.KeyDown)
			{
				HandleEscapeKey(_evtInOnGUI.keyCode);

				ToggleSortingWithAltKey(_evtInOnGUI.keyCode, _evtInOnGUI.alt);

				_evtInOnGUI.Use();
			}
			#endregion
		}

		private Dictionary<object, int> _selectionHistory = new Dictionary<object, int>();
		private Event _evtInOnGUI;

		private void UpdateSelectionHistory(object selectedItem)
		{
			if (_selectionHistory.ContainsKey(selectedItem))
			{
				_selectionHistory[selectedItem]++;
			}
			else
			{
				_selectionHistory[selectedItem] = 1;
			}
		}


		/// <summary>
		/// This method is called when an item is selected (Enter key is pressed)
		/// </summary>
		/// <param name="target"></param>
		private void HandleItemSelected(object target)
		{
			AddToSelectionHistory(target);  // Add this line
			UpdateSelectionHistory(target); // Update the history frequency
			_onItemSelectedCallback?.Invoke(target);
			this.Close();

			SavePersistentData();
		}


		/// <summary>
		/// Populates the FuzzySearchWindow with data and displays it.
		/// </summary>
		/// <param name="data">The collection of objects to be displayed in the window.</param>
		/// <param name="fieldExtractors">An array of functions to extract values from each object for display in the corresponding columns.</param>
		/// <param name="columnTitles">An array of strings representing the titles for each column in the list view.</param>
		/// <param name="onItemSelected">An optional callback function to be invoked when an item is selected in the list.</param>
		public void PopulateAndDisplay(IEnumerable<object> data, Func<object, object>[] fieldExtractors, string[] columnTitles, Action<object> onItemSelected = null)
		{
			if (data == null || fieldExtractors == null || columnTitles == null || fieldExtractors.Length != columnTitles.Length)
			{
				Debug.LogError("Invalid input for PopulateAndDisplay method.");
				return;
			}

			_noFilteredData = data.ToList();
			_listView.itemsSource = _noFilteredData;
			_fieldExtractors = fieldExtractors;
			_onItemSelectedCallback = onItemSelected;

			InitializeListViewColumns(columnTitles);
			ApplyCurrentSorting();
		}


		/// <summary>
		/// Removes a given prefix from a string.
		/// </summary>
		/// <param name="prefix">The prefix to remove.</param>
		/// <param name="haystack">The string from which to remove the prefix.</param>
		/// <returns>The string after removing the prefix.</returns>
		private string RemovePrefixString(string prefix, string haystack)
		{
			if (string.IsNullOrEmpty(prefix) || string.IsNullOrEmpty(haystack))
			{
				return haystack;
			}

			if (haystack.StartsWith(prefix))
			{
				return haystack.Remove(0, prefix.Length);
			}

			return haystack;
		}

		/// <summary>
		/// Handles the event when the 'Enter' key is pressed.
		/// </summary>
		/// <param name="keyCode">The key code of the pressed key.</param>
		private void HandleItemSelected(KeyCode keyCode)
		{
			//- 'Enter' key was pressed

			if (_listView.itemsSource == null || !_listView.itemsSource.Cast<object>().Any()) return;

			if (keyCode != KeyCode.Return && keyCode != KeyCode.KeypadEnter) return;

			//- Assuming the ListView is populated and has items,
			if (_listView.itemsSource == null || !_listView.itemsSource.Cast<object>().Any()) return;

			var selectedItem = _listView.selectedItem;
			if (selectedItem != null)
			{
				InvokeItemCallback(selectedItem as object);
				//CreateConsideration(selectedItem as object);
			}
		}

		private void InvokeItemCallback(object target)
		{
			AddToSelectionHistory(target);
			UpdateSelectionHistory(target);
			_onItemSelectedCallback?.Invoke(target);
			this.Close();

			SavePersistentData();
		}


		/// <summary>
		/// Handles selection of next and previous items in the list view based on key input.
		/// </summary>
		private void HandleArrowKeyNavigation(KeyDownEvent evt)
		{
			//- Exit early if the key is not an arrow key
			if (evt.keyCode != KeyCode.UpArrow && evt.keyCode != KeyCode.DownArrow)
			{
				return;
			}

			int maxIndex = _listView.itemsSource.Count - 1;
			int newIndex = _listView.selectedIndex;

			//- Check for Up arrow key press
			if (evt.keyCode == KeyCode.UpArrow)
			{
				// Decrement selected index to select the previous item
				if (newIndex > 0)
				{
					newIndex -= 1;
				}
				else
				{
					// Already at the first item, no need to update further
					evt.PreventDefault();
					return;
				}
			}
			//- Check for Down arrow key press
			else if (evt.keyCode == KeyCode.DownArrow)
			{
				// Increment selected index to select the next item
				if (newIndex < maxIndex)
				{
					newIndex += 1;
				}
				else
				{
					//- Already at the last item, no need to update further
					evt.PreventDefault();
					return;
				}
			}

			_listView.selectedIndex = newIndex;
			evt.PreventDefault();
		}


		/// <summary>
		/// Filters the input list based on the provided filter text using fuzzy matching.
		/// </summary>
		/// <param name="filterText">The text to use for filtering the list.</param>
		/// <param name="inputList">The list of objects to be filtered.</param>
		/// <param name="columnIndex">The index of the column to use for filtering (default is 0).</param>
		/// <returns>A new list containing the filtered objects.</returns>
		private List<object> FuzzyFilterList(string filterText, List<object> inputList, int columnIndex = 0)
		{
			if (_fieldExtractors == null || columnIndex >= _fieldExtractors.Length)
			{
				return inputList;
			}

			if (filterText == "") return inputList;

			return inputList
				.Select(item => {
					string candidate = _fieldExtractors[columnIndex](item).ToString();
					candidate = RemovePrefixString(_prefixToRemove, candidate).ToLower();
					return new { Item = item, Candidate = candidate };
				})
				.Where(x => {
					int j = 0;
					string text = filterText.ToLower();
					for (int i = 0; i < x.Candidate.Length; i++)
					{
						if (j < text.Length && text[j] == x.Candidate[i]) j++;
						if (j == text.Length) return true;
					}
					return false;
				})
				.Select(x => x.Item)
				.ToList();
		}


		/// <summary>
		/// Initialize UI elements
		/// </summary>
		private void FindUIElements()
		{
			_historySortToggle = rootVisualElement.Q<Toggle>("historySortToggle");
			_listView = rootVisualElement.Q<MultiColumnListView>("FuzzySearchWindow");
			_searchInputField = rootVisualElement.Q<TextField>("searchInputField");
			_prefixToRemoveInputField = rootVisualElement.Q<TextField>("prefixToRemove");
			_favoriteSortToggle = rootVisualElement.Q<Toggle>("favoriteSortToggle");

			RegisterEventListeners();
		}

		/// <summary>
		/// Setting up event handlers
		/// </summary>
		private void RegisterEventListeners()
		{
			_searchInputField.RegisterValueChangedCallback(UpdateSearchFilter);
			_searchInputField.RegisterCallback<KeyDownEvent>(ProcessSearchInputKeyDown);
			_prefixToRemoveInputField.RegisterValueChangedCallback(UpdatePrefixFilter);
			rootVisualElement.RegisterCallback<KeyDownEvent>(ProcessGlobalKeyDown);
		}


		private void UpdateSorting(SortType sortType)
		{
			currentSortType = sortType;
			ApplyCurrentSorting();
		}


		private void ProcessGlobalKeyDown(KeyDownEvent evt)
		{
			//- Handle Submiting the choice.
			HandleItemSelected(evt.keyCode);
			HandleEscapeKey(evt.keyCode);
			ToggleSortingWithAltKey(evt.keyCode, evt.altKey);
		}

		/// <summary>
		/// Handling KeyDown Event in Search Input Field
		/// </summary>
		/// <param name="evt"></param>
		private void ProcessSearchInputKeyDown(KeyDownEvent evt)
		{
			//- Handle Submiting the choice.
			HandleItemSelected(evt.keyCode);

			HandleArrowKeyNavigation(evt);

			HandleEscapeKey(evt.keyCode);

			ToggleSortingWithAltKey(evt.keyCode, evt.altKey);

		}

		private void ToggleSortingWithAltKey(KeyCode keyCode, bool isAltPressed)
		{
			if (!isAltPressed) return;

			//- Settings
			var KeyForToggleSortByFavorite = KeyCode.F;
			var KeyForToggleSortByHistory = KeyCode.S;

			//- Toogle SortBy Favorite
			if (keyCode == KeyForToggleSortByFavorite)
			{
				_favoriteSortToggle.value = !_favoriteSortToggle.value;
			}
			//- Toogle SortBy History
			else if (keyCode == KeyForToggleSortByHistory)
			{
				_historySortToggle.value = !_historySortToggle.value;
			}
		}

		private void HandleEscapeKey(KeyCode keyCode)
		{
			if (keyCode != KeyCode.Escape) return;
			this.Close();
		}

		private void UpdatePrefixFilter(ChangeEvent<string> evt)
		{
			_prefixToRemove = _prefixToRemoveInputField.value;
			ApplyPrefixFilter(_prefixToRemove);
			var filteredData = GetSortedAndFilteredData();
			RefreshListView(filteredData);
		}

		/// <summary>
		/// Handles the event when the search value changes.
		/// </summary>
		/// <param name="evt">The change event for the search input field.</param>
		private void UpdateSearchFilter(ChangeEvent<string> evt)
		{
			SetSearchQuery(evt);
			var filteredData = GetSortedAndFilteredData();
			RefreshListView(filteredData);
		}

		/// <summary>
		/// Updates the filter text based on the input event.
		/// </summary>
		/// <param name="evt">The change event for the search input field.</param>
		private void SetSearchQuery(ChangeEvent<string> evt)
		{
			_filterTextValue = evt.newValue.ToLower();
		}

		private void RefreshListView(List<object> filteredData)
		{
			_filteredData = filteredData;
			_listView.itemsSource = _filteredData;
			_listView.SetSelection(0);
			_listView.Rebuild();
		}

		/// <summary>
		/// Filters the list of planets based on the current filter text.
		/// </summary>
		/// <returns>A list of filtered planets.</returns>
		private List<object> GetSortedAndFilteredData()
		{
			var filteredData = FuzzyFilterList(_filterTextValue, _noFilteredData);
			filteredData = SortFilteredData(filteredData);
			return filteredData;
		}


		/// <summary>
		/// Transforms the data array into a list.
		/// </summary>
		/// <param name="data">The data array to be transformed.</param>
		/// <returns>A list containing the transformed data.</returns>
		private List<object> ConvertToArrayToList(object[] data)
		{
			return data.ToList();
		}

		/// <summary>
		/// Sets up the columns for the list view, including titles, widths, and cell rendering.
		/// </summary>
		/// <param name="columnTitles">An array of strings representing the titles for each column.</param>
		private void InitializeListViewColumns(string[] columnTitles)
		{
			if (_listView == null || _listView.columns == null || _fieldExtractors == null || columnTitles == null || _fieldExtractors.Length != columnTitles.Length)
			{
				Debug.LogError("Invalid input for InitializeListViewColumns method.");
				return;
			}

			_listView.columns.Clear(); // Clear existing columns before adding new ones

			for (int i = 0; i < _fieldExtractors.Length; i++)
			{
				int columnIndex = i;

				_listView.columns.Add(new Column {
					title = columnTitles[columnIndex],
					width = 325 + FavoriteIconWidth,
					makeCell = () => {
						if (columnIndex == 0) // Only the first column has the favorite icon
						{
							VisualElement container = new VisualElement();
							container.style.flexDirection = FlexDirection.Row;

							Label nameLabel = new Label();
							nameLabel.name = "NameLabel";
							nameLabel.style.flexGrow = 1;

							VisualElement favoriteIcon = new VisualElement();
							favoriteIcon.name = "FavoriteIcon";
							favoriteIcon.style.backgroundImage = Background.FromTexture2D(favoriteIconOff);
							favoriteIcon.style.width = FavoriteIconWidth;
							favoriteIcon.style.height = FavoriteIconHeight;
							// Use a lambda to capture the current item's index
							favoriteIcon.RegisterCallback<ClickEvent>(evt => {
								int currentIndex = _listView.selectedIndex;
								if (currentIndex >= 0 && currentIndex < _filteredData.Count)
								{
									ToggleFavoriteStatus(_filteredData[currentIndex], favoriteIcon);
								}
							});

							container.Add(nameLabel);
							container.Add(favoriteIcon);

							return container;
						}
						else
						{
							return new Label();
						}
					},
					bindCell = (VisualElement element, int index) => {
						if (_filteredData == null || index >= _filteredData.Count)
						{
							return;
						}

						if (columnIndex == 0)
						{
							var container = (VisualElement)element;
							var nameLabel = container.Q<Label>("NameLabel");
							var favoriteIcon = container.Q<VisualElement>("FavoriteIcon");

							var itemObject = _filteredData[index];
							nameLabel.text = RemovePrefixString(_prefixToRemove, _fieldExtractors[columnIndex](itemObject).ToString());

							SetFavoriteIcon(itemObject, favoriteIcon);
						}
						else
						{
							var label = (Label)element;
							label.text = RemovePrefixString(_prefixToRemove, _fieldExtractors[columnIndex](_filteredData[index]).ToString());
						}
					}
				});
			}
		}

		private bool IsFavorite(object item)
		{
			string identifier = SerializeObject(item);
			return _favoriteItems.Contains(identifier);
		}

		private void ToggleFavorite(object item, bool isFavorite)
		{
			string identifier = SerializeObject(item);

			if (isFavorite)
			{
				if (!_favoriteItems.Contains(identifier))
				{
					_favoriteItems.Add(identifier);
				}
			}
			else
			{
				_favoriteItems.Remove(identifier);
			}

			// After toggling, we should resort the data if we're currently sorting by favorites
			if (currentSortType == SortType.ByFavorite)
			{
				ApplyCurrentSorting();
			}

			// Save the updated favorite items
			SavePersistentData();
		}

		/// <summary>
		/// Configures default values for UI elements.
		/// </summary>
		private void ApplyDefaultSettings()
		{
			SetInitialPrefixFilter();
			SelectFirstItemOnOpen();
			FocusSearchInputOnOpen();
		}

		/// <summary>
		/// Sets the default value for the Prefix To Remove input field.
		/// </summary>
		private void SetInitialPrefixFilter()
		{
			// TODO: Use settings to have persistence value.
			_prefixToRemoveInputField.value = _prefixToRemove;
		}

		/// <summary>
		/// Automatically selects the first item in the list view.
		/// </summary>
		private void SelectFirstItemOnOpen()
		{
			_listView.SetSelection(0);
		}

		/// <summary>
		/// Sets focus to the search input field.
		/// </summary>
		private void FocusSearchInputOnOpen()
		{
			_searchInputField.Focus();
		}

		/// <summary>
		/// Adds items to the results list based on their Levenshtein distance to the search query.
		/// </summary>
		/// <param name="data">The data list to search through.</param>
		/// <param name="searchQuery">The search query.</param>
		/// <param name="someThreshold">The Levenshtein distance threshold for including items in the results.</param>
		/// <returns>A new list of items sorted by their Levenshtein distance to the search query.</returns>
		private List<object> FuzzySearchByLevenshtein(List<object> data, string searchQuery, int columnIndex = 0, float someThreshold = 0.2f)
		{
			if (_fieldExtractors == null || columnIndex >= _fieldExtractors.Length)
			{
				return new List<object>();
			}

			//- Initialize the results list
			List<object> searchResults = new List<object>();

			foreach (object item in data)
			{

				string itemString = _fieldExtractors[columnIndex](item).ToString();
				itemString = RemovePrefixString(_prefixToRemove, itemString).ToLower(); //- see 020231018194614.mp4

				float levenshteinDistance = LevenshteinDistance(itemString, searchQuery);  // You need to implement or import this function

				Debug.Log($"Item: {itemString}, Levenshtein Distance: {levenshteinDistance}");

				//- Add a condition to include the item based on its Levenshtein distance
				if (levenshteinDistance >= someThreshold)  // Replace 'someThreshold' with an appropriate value
				{
					//- Insert the item into the sorted list
					bool inserted = false;
					for (int i = 0; i < searchResults.Count; i++)
					{
						float existingItemDistance = LevenshteinDistance(searchResults[i].ToString().ToLower(), searchQuery);
						if (existingItemDistance > levenshteinDistance)
						{
							searchResults.Insert(i, item);
							inserted = true;
							break;
						}
					}
					if (!inserted)
					{
						searchResults.Add(item);
					}
				}
			}

			return searchResults;
		}

		public static float LevenshteinDistance(string str, string searchQuery)
		{
			if (str == "" || searchQuery == "")
			{
				return LevenshteinEmptyStringScore;
			}
			int tableLength = searchQuery.Length;
			int[] table = new int[tableLength];
			int[] table2 = new int[tableLength];

			for (int j = 0; j < tableLength; j++)
			{
				table[j] = j;
			}

			for (int i = 0; i < str.Length - 1; i++)
			{
				table2[0] = i + 1;
				for (int j = 0; j < tableLength - 1; j++)
				{
					int delCost = table[j + 1] + 1;
					int insCost = table2[j] + 1;
					int substituteCost = table[j];

					if (str[i] != searchQuery[j])
					{
						substituteCost += 1;
					}

					table2[j + 1] = Mathf.Min(delCost, insCost, substituteCost);
				}
				Array.Copy(table2, table, tableLength);
			}

			return 1.0f - (float)table[searchQuery.Length - 1] / Mathf.Max(str.Length, searchQuery.Length);
		}

		/// <summary>
		/// Sorts the data in the list view based on the currently selected sorting criteria.
		/// </summary>
		private void ApplyCurrentSorting()
		{
			if (!Enum.IsDefined(typeof(SortType), currentSortType)) return;

			if (currentSortType == SortType.ByHistory)
			{
				SortByLastSelected();
			}
			else if (currentSortType == SortType.ByFavorite)
			{
				SortByIsFavorite();
			}
			else
			{
				SortByFirstColumn();
			}
		}

		private void SortByFirstColumn()
		{
			SortListBy(item => _fieldExtractors[0](item).ToString());
		}

		/// <summary>
		/// Sorts the data source based on the order items were last selected.
		/// </summary>
		private void SortByLastSelected()
		{
			// Reverse the _selectionOrder list so that the most recently selected items come first
			List<string> reversedSelectionOrder = new List<string>(_selectionOrder);
			reversedSelectionOrder.Reverse();

			SortListBy(item => {
				string identifier = SerializeObject(item);
				int index = reversedSelectionOrder.IndexOf(identifier);
				return index == -1 ? int.MaxValue : index;
			});
		}

		private void SortByIsFavorite()
		{
			SortListBy(item => {
				string identifier = SerializeObject(item);
				return _favoriteItems.Contains(identifier) ? 0 : 1; // Favorites first
			});
		}

		/// <summary>
		/// Sorts the data source based on item selection history.
		/// </summary>
		private void SortBySelectionFrequency()
		{
			SortListBy(item => {
				string identifier = SerializeObject(item);
				_selectionHistory.TryGetValue(identifier, out int frequency);
				return frequency;
			});
		}

		private void SortDataBySelectionFrequency()
		{
			_noFilteredData = _noFilteredData.OrderByDescending(item => {
				_selectionHistory.TryGetValue(item, out int frequency);
				return frequency;
			}).ToList();
		}

		private void AddToSelectionHistory(object selectedItem)
		{
			string identifier = SerializeObject(selectedItem);
			_selectionOrder.Remove(identifier);
			_selectionOrder.Add(identifier);
		}

		#region-- Favorite
		/// <summary>
		/// Sets the favorite icon (on or off) for the given item in the list view.
		/// </summary>
		/// <param name="itemIdentifier">The object representing the item in the list.</param>
		/// <param name="favoriteIcon">The VisualElement representing the favorite icon.</param>
		void SetFavoriteIcon(object itemIdentifier, VisualElement favoriteIcon)
		{
			Texture2D iconTexture;
			string identifier = SerializeObject(itemIdentifier); // Get string identifier

			if (_favoriteItems.Contains(identifier))
			{
				iconTexture = favoriteIconOn;
			}
			else
			{
				iconTexture = favoriteIconOff;
			}

			favoriteIcon.style.backgroundImage = Background.FromTexture2D(iconTexture);
		}

		private void HandleFavoriteIconClick(ClickEvent evt, object item, VisualElement favoriteIcon)
		{
			bool newFavoriteStatus = !IsFavorite(item);
			ToggleFavorite(item, newFavoriteStatus);
			ToggleFavoriteStatus(item, favoriteIcon);
		}

		/// <summary>
		/// Toggles the favorite status of an item and updates the corresponding icon in the list view.
		/// </summary>
		/// <param name="itemIdentifier">The object representing the item in the list.</param>
		/// <param name="favoriteIcon">The VisualElement representing the favorite icon.</param>
		void ToggleFavoriteStatus(object itemIdentifier, VisualElement favoriteIcon)
		{
			Texture2D iconTexture;
			string identifier = SerializeObject(itemIdentifier); // Get string identifier

			// Toggle favorite status
			if (_favoriteItems.Contains(identifier))
			{
				_favoriteItems.Remove(identifier);
				iconTexture = favoriteIconOff;
			}
			else
			{
				_favoriteItems.Add(identifier);
				iconTexture = favoriteIconOn;
			}

			favoriteIcon.style.backgroundImage = Background.FromTexture2D(iconTexture);
		}

		private void AttachFavoriteIconClickListener(VisualElement favoriteIcon, object itemObject)
		{
			favoriteIcon.userData = itemObject;  // Store object reference in the element's userData
			favoriteIcon.UnregisterCallback<ClickEvent>(OnFavoriteIconClicked);
			favoriteIcon.RegisterCallback<ClickEvent>(OnFavoriteIconClicked);
		}

		private void OnFavoriteIconClicked(ClickEvent evt)
		{
			var favoriteIcon = evt.target as VisualElement;
			var itemObject = favoriteIcon.userData;  // Retrieve object reference from userData
			ToggleFavoriteStatus(itemObject, favoriteIcon);
		}
		#endregion

		private void SetupUI()
		{
			FindUIElements();
			SetupFavoriteSorting();
			SetupHistorySorting();
			ApplyDefaultSettings();
		}
		private void SortListBy(Func<object, object> sortingFunc)
		{
			if (_noFilteredData == null)
			{
				return;
			}

			_noFilteredData = _noFilteredData.OrderBy(sortingFunc).ToList();
			RefreshListView(GetSortedAndFilteredData());
		}

		/// <summary>
		/// Sorts the filtered list based on the active sort toggles (favorite and history).
		/// </summary>
		/// <param name="filteredList">The list of objects to be sorted.</param>
		/// <returns>The sorted list of objects.</returns>
		private List<object> SortFilteredData(List<object> filteredList)
		{
			if (_favoriteSortToggle.value)
			{
				filteredList = filteredList.OrderByDescending(item => {
					string identifier = SerializeObject(item);
					return _favoriteItems.Contains(identifier); // Prioritize favorites
				}).ToList();
			}

			if (_historySortToggle.value)
			{
				filteredList = filteredList.OrderByDescending(item => {
					string identifier = SerializeObject(item);
					_selectionHistory.TryGetValue(identifier, out int frequency);
					return frequency;
				}).ToList();
			}

			return filteredList;
		}

		private void SetupFavoriteSorting()
		{
			_favoriteSortToggle.RegisterValueChangedCallback(evt => {
				currentSortType = evt.newValue ? SortType.ByFavorite : SortType.Alphabetical;
				ApplyCurrentSorting();
			});
		}

		private void SetupHistorySorting()
		{
			_historySortToggle.RegisterValueChangedCallback(evt => {
				currentSortType = evt.newValue ? SortType.ByHistory : SortType.Alphabetical;
				ApplyCurrentSorting();
			});
		}

		private string ApplyPrefixFilter(string input)
		{
			return RemovePrefixString(_prefixToRemove, input).ToLower();
		}

		/// <summary>
		/// Ensures that the data file exists at the specified path. 
		/// Creates the file and necessary directories if it doesn't exist.
		/// </summary>
		/// <param name="filePath">The path to the data file.</param>
		void EnsureDataFileExists(string filePath)
		{
			// Check if the file exists
			if (!File.Exists(filePath))
			{
				// If the file doesn't exist, create the directories and the file
				string directoryPath = Path.GetDirectoryName(filePath);
				Directory.CreateDirectory(directoryPath);

				File.WriteAllText(filePath, "{}");  // Creates the file and writes an empty JSON object to it
				AssetDatabase.Refresh();  // Refresh the AssetDatabase to update the Unity editor
			}
		}

		/// <summary>
		/// Prepares a FuzzySearchWindow for a given field.
		/// </summary>
		/// <param name="field">The field to prepare the fuzzy search for.</param>
		/// <param name="columnTitles">The titles for the columns in the fuzzy search window.</param>
		/// <param name="propertyNames">The names of the properties to display in each column.</param>
		/// <param name="defaultValues">The default values to display when a property is null or "None".</param>
		/// <returns>An action to be executed when the field is clicked.</returns>
		public static EventCallback<ClickEvent> PrepareFuzzySearch(
			object field,
			string[] columnTitles,
			string[] propertyNames,
			string[] defaultValues,
			string[] valueWhenObjIsNull = null)
			
		{
			valueWhenObjIsNull ??= new string[2] { "", "" };

			var choicesProperty = field.GetType().GetProperty("choices");
			var valueProperty = field.GetType().GetProperty("value");

			if (choicesProperty == null || valueProperty == null)
			{
				Debug.LogError("Field does not have required properties: 'choices' and 'value'");
				return null;
			}

			return evt =>
			{
				var choices = choicesProperty.GetValue(field);

				IEnumerable<object> datas;

				if (choices is IEnumerable<object> enumerable)
				{
					datas = enumerable;
				}
				else if (choices is IEnumerable nonGenericEnumerable)
				{
					datas = nonGenericEnumerable.Cast<object>();
				}
				else
				{
					Debug.LogError("Choices property is not enumerable");
					return;
				}

				var fieldExtractors = new Func<object, object>[propertyNames.Length];
				for (int i = 0; i < propertyNames.Length; i++)
				{
					int index = i;
					fieldExtractors[i] = obj =>
					{
						if (obj == null) { 
							return valueWhenObjIsNull[index];
						}

						var propertyInfo = obj.GetType().GetProperty(propertyNames[index]);
						if (propertyInfo != null)
						{
							try
							{
								var value = propertyInfo.GetValue(obj)?.ToString();
								return string.IsNullOrEmpty(value) || value == "None" ? defaultValues[index] : value;
							}
							catch (Exception)
							{
								return defaultValues[index];
							}
						}
						string message = $"Property '{propertyNames[index]}' not found on object of type '{obj.GetType()}'";
						Debug.LogWarning(message);
						return message;
					};
				}

				var fuzzySearchWindow = EditorWindow.GetWindow<FuzzySearchWindow>(false);
				fuzzySearchWindow.PopulateAndDisplay(datas, fieldExtractors, columnTitles, chosenItem =>
				{
					valueProperty.SetValue(field, chosenItem);
				});
			};
		}
	}

	#region-- TODO to put in separate files. We keep all here for now to easy upload to chatGPT.
	#endregion
}