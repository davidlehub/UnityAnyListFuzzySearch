using System.Collections;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using System.IO;
using Unity.Plastic.Newtonsoft.Json;


public class FuzzySearchWindow : EditorWindow
{
	public enum SortType
	{
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
	private List<object> _selectionOrder = new();
	private List<object> _favoriteItems = new();

	private Toggle _historySortToggle;
	private Toggle _favoriteSortToggle;
	private List<object> _noFilteredData;
	private List<object> _filteredData;
	private string _prefixToRemove = "Consider";
	private string _filterTextValue = "";
	private MultiColumnListView _listView;
	private TextField _searchInputField;
	private TextField _prefixToRemoveInputField;
	private Func<object, object>[] _fieldExtractors;
	private Action<object> _onItemSelectedCallback;
	

	[Serializable]
	public class PersistentData
	{
		public string SerializedFavorites;
		// public List<string> SelectionOrder;
		// public string SerializedSelectionHistory;  // New field
		// You can add more fields here
	}
		
private string SerializeObject(object obj)
{
    string typeStr = obj.GetType().AssemblyQualifiedName;
    string jsonStr = JsonConvert.SerializeObject(obj);
    return $"{typeStr}|{jsonStr}";
}

private object DeserializeObject(string str)
{
    string[] parts = str.Split(new char[] {'|'}, 2);
    if (parts.Length != 2)
    {
        return null;
    }

    string typeStr = parts[0];
    string jsonStr = parts[1];

    Type type = Type.GetType(typeStr);
    if (type == null)
    {
        return null;
    }

    return JsonConvert.DeserializeObject(jsonStr, type);
}



	[Serializable]
	public class SerializedObject
	{
		public string TypeName;
		public string Data;
		public bool IsFavorite;
	}

		
	public void OnEnable()
	{
		// LoadPersistentData();
		
		if (_uxml == null) return;
		
		DataPath = DataFolderPath + "/" + "FuzzySearchWindowData.json";
		
		_noFilteredData = new List<object>();
		
		// YourIconTexture2D = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Editor/BetterSearch/Resources/emptyFavorite.png");
		
		_uxml.CloneTree(rootVisualElement);
		
		InitializeUIElements(); // Initialize UI elements
		
		LoadPersistentData();

	}	
	
	private void SavePersistentData()
	{
		try
		{
			// Create a new PersistentData object
			PersistentData data = new PersistentData
			{
				// Serialize _favoriteItems using custom logic
				SerializedFavorites = JsonConvert.SerializeObject(_favoriteItems.Select(SerializeObject)),			
				// Serialize _selectionHistory  // New line
				// SerializedSelectionHistory = JsonConvert.SerializeObject(_selectionHistory),
				// You can serialize more fields here
			};

			// Serialize the entire PersistentData object to a JSON string
			string json = JsonConvert.SerializeObject(data, Formatting.Indented);
			
			//- Create folder if not exist
			CheckAndCreateFile(DataPath);
			
			// Write the JSON string to the data path
			File.WriteAllText(DataPath, json);
		}
		catch (Exception e)
		{
			// Log any exceptions that might occur
			Debug.LogError("An error occurred while saving persistent data: " + e.Message);
		}
	}

	private void LoadPersistentData()
	{

		// InitializeUIElements(); // Initialize UI elements
		if (!File.Exists(DataPath))
		{
			return;
		}
		
		string json = File.ReadAllText(DataPath);

		// Check if json is null or empty
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
		// _selectionOrder = data.SelectionOrder.Select(DeserializeObject).ToList();
		
		// Deserialize _favoriteItems using custom logic
		List<string> favoriteItemsSerialized = JsonConvert.DeserializeObject<List<string>>(data.SerializedFavorites);
		_favoriteItems = favoriteItemsSerialized.Select(DeserializeObject).ToList();
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
		InitializeUI();
    }

	public void OnGUI()
    {
		//- Handle Submiting the choice. When user press, for example Enter key, while the Window is focus.
		/* CommentOut, bcz maybe not desired
		if (Event.current.type == EventType.KeyDown) 
		{
			HandleSubmitKeyPressed(Event.current.keyCode);
		}
		*/
		
		#region-- Pressing alt + another key to toggle favorite or history sorting. (vid 020231024120602 02)
		// Get the current event
		_evtInOnGUI = Event.current;

		// Check if the event is a key down event
		if (_evtInOnGUI.type == EventType.KeyDown)
		{
			HandleCancelOperationKeyPressed(_evtInOnGUI.keyCode);
			
			HandleToggleSortingKeyPressed(_evtInOnGUI.keyCode, _evtInOnGUI.alt);
			
			_evtInOnGUI.Use();			
		}		
		#endregion
    }

	// private bool _isSortedByHistory = false; // Set this to true to enable sorting by history
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
	private void CreateConsideration(object target)
	{
		UpdateSelectionOrder(target);  // Add this line
		UpdateSelectionHistory(target); // Update the history frequency
		_onItemSelectedCallback?.Invoke(target);
		this.Close();
		
		SavePersistentData();
	}


	public void Summon(IEnumerable<object> data, Func<object, object>[] fieldExtractors, Action<object> onItemSelected = null)
	{
		if (data == null || fieldExtractors == null) return;
		
		_noFilteredData = TransformData((object[])data);

		_listView.itemsSource = _noFilteredData;
		_fieldExtractors = fieldExtractors;
		SetupColumnsTypeAndBinding();
		_onItemSelectedCallback = onItemSelected;  
		
		//- Sort by history if the flag is set
		SortData();
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
	private void HandleSubmitKeyPressed(KeyCode keyCode)
	{
		//- 'Enter' key was pressed
		
		if (_listView.itemsSource == null || !_listView.itemsSource.Cast<object>().Any()) return;

		if (keyCode != KeyCode.Return && keyCode != KeyCode.KeypadEnter) return;

		//- Assuming the ListView is populated and has items,
		if (_listView.itemsSource == null || !_listView.itemsSource.Cast<object>().Any()) return;

		var selectedItem = _listView.selectedItem;
		if (selectedItem != null)
		{
			CreateConsideration(selectedItem as object);
		}
	}
	
	
	/// <summary>
	/// Handles selection of next and previous items in the list view based on key input.
	/// </summary>
	private void HandleNextAndPreviousItemSelection(KeyDownEvent evt)
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
	/// Filters and sorts the list based on the search query.
	/// </summary>
	/// <param name="filterText">The search query.</param>
	/// <param name="inputList">The list to be filtered.</param>
	/// <param name="columnIndex">The column index to filter on.</param>
	/// <returns>A filtered and sorted list.</returns>
	private List<object> FilterData(string filterText, List<object> inputList, int columnIndex = 0)
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
	private void InitializeUIElements()
	{
		_historySortToggle = rootVisualElement.Q<Toggle>("historySortToggle");
		_listView = rootVisualElement.Q<MultiColumnListView>("FuzzySearchWindow");
		_searchInputField = rootVisualElement.Q<TextField>("searchInputField");
		_prefixToRemoveInputField = rootVisualElement.Q<TextField>("prefixToRemove");
		_favoriteSortToggle = rootVisualElement.Q<Toggle>("favoriteSortToggle");

		SetupEventHandlers();
	}
	
	/// <summary>
	/// Setting up event handlers
	/// </summary>
	private void SetupEventHandlers()
	{
		_searchInputField.RegisterValueChangedCallback(HandleSearchValueChanged);
		_searchInputField.RegisterCallback<KeyDownEvent>(HandleSearchInputKeyDown);
		_prefixToRemoveInputField.RegisterValueChangedCallback(HandlePrefixValueChanged);
		rootVisualElement.RegisterCallback<KeyDownEvent>(HandleAnyUiElementKeyDown);
	}


	private void HandleSortOptionChanged(SortType sortType)
	{
		currentSortType = sortType;
		SortData();
	}


	private void HandleAnyUiElementKeyDown(KeyDownEvent evt)
	{
		//- Handle Submiting the choice.
		HandleSubmitKeyPressed(evt.keyCode);
		HandleCancelOperationKeyPressed(evt.keyCode);
		HandleToggleSortingKeyPressed(evt.keyCode, evt.altKey);
	}

	/// <summary>
	/// Handling KeyDown Event in Search Input Field
	/// </summary>
	/// <param name="evt"></param>
	private void HandleSearchInputKeyDown(KeyDownEvent evt)
	{
		//- Handle Submiting the choice.
		HandleSubmitKeyPressed(evt.keyCode);
		
		HandleNextAndPreviousItemSelection(evt);
		
		HandleCancelOperationKeyPressed(evt.keyCode);
		
		HandleToggleSortingKeyPressed(evt.keyCode, evt.altKey);
		
	}

	private void HandleToggleSortingKeyPressed(KeyCode keyCode, bool isAltPressed)
	{
		//- (vid 020231024103327)
		
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


	// private void HandleToggleSortingKeyPressed(KeyDownEvent evt)
	// {
	// 	//- (vid 020231024103327)
		
	// 	if (!evt.altKey) return;	
	// 	//- Settings
	// 	var KeyForToggleSortByFavorite = KeyCode.F;
	// 	var KeyForToggleSortByHistory = KeyCode.S;

	// 	//- Toogle SortBy Favorite
	// 	if (evt.keyCode == KeyForToggleSortByFavorite)
	// 	{
	// 		_favoriteSortToggle.value = !_favoriteSortToggle.value;
	// 		// evt.StopPropagation(); // Stop the event from propagating further
	// 	}
	// 	//- Toogle SortBy History
	// 	else if (evt.keyCode == KeyForToggleSortByHistory)
	// 	{
	// 		_historySortToggle.value = !_historySortToggle.value;
	// 		// evt.StopPropagation(); // Stop the event from propagating further
	// 	}
		
	// 	evt.StopPropagation(); // Stop the event from propagating further
		
	// }

	private void HandleCancelOperationKeyPressed(KeyCode keyCode)
	{
		if (keyCode != KeyCode.Escape) return;
		this.Close();
		// throw new NotImplementedException();
	}

	private void HandlePrefixValueChanged(ChangeEvent<string> evt)
	{
		_prefixToRemove = _prefixToRemoveInputField.value;
		ProcessPrefix(_prefixToRemove);
		var filteredData = GetFilteredData();
		UpdateListView(filteredData);
	}

	/// <summary>
	/// Handles the event when the search value changes.
	/// </summary>
	/// <param name="evt">The change event for the search input field.</param>
	private void HandleSearchValueChanged(ChangeEvent<string> evt)
	{
		UpdateFilterText(evt);
		var filteredData = GetFilteredData();
		UpdateListView(filteredData);	
	}
	
	/// <summary>
	/// Updates the filter text based on the input event.
	/// </summary>
	/// <param name="evt">The change event for the search input field.</param>
	private void UpdateFilterText(ChangeEvent<string> evt)
	{
		_filterTextValue = evt.newValue.ToLower();
	}	
	
	private void UpdateListView(List<object> filteredData)
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
	private List<object> GetFilteredData()
	{
		var filteredData = FilterData(_filterTextValue, _noFilteredData);
		filteredData = SortFilteredData(filteredData);
		return filteredData;
	}


	/// <summary>
	/// Transforms the data array into a list.
	/// </summary>
	/// <param name="data">The data array to be transformed.</param>
	/// <returns>A list containing the transformed data.</returns>
	private List<object> TransformData(object[] data)
	{
		return data.ToList();
	}
	
	/// <summary>
	/// Sets up the columns type and binding for the list view.
	/// </summary>
	private void SetupColumnsTypeAndBinding()
	{
		if (_listView == null || _listView.columns == null || _fieldExtractors == null)
		{
			return;
		}
		
		
		// Handle the first column separately to add the "Favorite" icon
		_listView.columns[0].makeCell = () =>
		{
			VisualElement container = new VisualElement();
			container.style.flexDirection = FlexDirection.Row;
			
			Label nameLabel = new Label();
			nameLabel.name = "NameLabel";
			nameLabel.style.flexGrow = 1;

			VisualElement favoriteIcon = new VisualElement();
			favoriteIcon.name = "FavoriteIcon";
			// Assuming YourIconTexture2D is a Texture2D object you've loaded
			favoriteIcon.style.backgroundImage = Background.FromTexture2D(favoriteIconOff); 
			favoriteIcon.style.width = FavoriteIconWidth;
			favoriteIcon.style.height = FavoriteIconHeight;
			favoriteIcon.RegisterCallback<ClickEvent>(evt => OnFavoriteIconClicked(nameLabel.text, favoriteIcon));

			InitializeFavoriteIcon(nameLabel.text, favoriteIcon);
			
			container.Add(nameLabel);
			container.Add(favoriteIcon);
			
			return container;
		};


		_listView.columns[0].bindCell = (VisualElement element, int index) => 
		{
			if (_filteredData == null || index >= _filteredData.Count)
			{
				return;
			}
			
			var itemObject = _filteredData[index];  // This should give you the object reference
			var nameLabel = element.Q<Label>("NameLabel");
			var fieldExtracted = _fieldExtractors[0](itemObject).ToString();
			fieldExtracted = RemovePrefixString(_prefixToRemove, fieldExtracted);
			nameLabel.text = fieldExtracted;
			
			var favoriteIcon = element.Q<VisualElement>("FavoriteIcon");
			
			UpdateFavoriteIconCallback(favoriteIcon, itemObject);
			
			InitializeFavoriteIcon(itemObject, favoriteIcon);
		};

		// Handle other columns as you were doing before
		for (int i = 1; i < _fieldExtractors.Length; i++)
		{
			int columnIndex = i;
			
			_listView.columns[columnIndex].makeCell = () => new Label();
			_listView.columns[columnIndex].bindCell = (VisualElement element, int index) => {
				var fieldExtracted = _fieldExtractors[columnIndex](_filteredData[index]).ToString();
				fieldExtracted = RemovePrefixString(_prefixToRemove, fieldExtracted);
				(element as Label).text = fieldExtracted;
			};
		}
	}
	
	/// <summary>
	/// Configures default values for UI elements.
	/// </summary>
	private void ConfigureDefaultValues()
	{
		SetPrefixToRemoveDefaultValue();
		AutoSelectFirstItem();
		AutoFocusSearchInputField();
	}	
	
	/// <summary>
	/// Sets the default value for the Prefix To Remove input field.
	/// </summary>
	private void SetPrefixToRemoveDefaultValue()
	{
		// TODO: Use settings to have persistence value.
		_prefixToRemoveInputField.value = _prefixToRemove;
	}	
	
	/// <summary>
	/// Automatically selects the first item in the list view.
	/// </summary>
	private void AutoSelectFirstItem()
	{
		_listView.SetSelection(0);
	}	
	
	/// <summary>
	/// Sets focus to the search input field.
	/// </summary>
	private void AutoFocusSearchInputField()
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
	private List<object> AddItemsBasedOnLevenshteinDistance(List<object> data, string searchQuery, int columnIndex = 0, float someThreshold = 0.2f)
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
	/// Sorts the data based on the current sort options (_sortByHistory).
	/// </summary>
	private void SortData()
	{
		if (!Enum.IsDefined(typeof(SortType), currentSortType)) return;

		if (currentSortType == SortType.ByHistory)
		{
			SortBySelectionOrder();
			// SortByHistory();
		}
		else if (currentSortType == SortType.ByFavorite)
		{
			SortByFavorite();
		}
		else
		{
			SortByAlphabet();
		}
	}
		
	private void SortByAlphabet()
	{
		SortDataGeneral(item => _fieldExtractors[0](item).ToString());
	}

	/// <summary>
	/// Sorts the data source based on the order items were last selected.
	/// </summary>
	/// <remarks>
	/// Items are sorted in the order they were last selected, with the most recently selected items appearing last.
	/// </remarks>
	private void SortBySelectionOrder()
	{
		// Reverse the _selectionOrder list so that the most recently selected items come first
		List<object> reversedSelectionOrder = new List<object>(_selectionOrder);
		reversedSelectionOrder.Reverse();

		SortDataGeneral(item => 
		{
			int index = reversedSelectionOrder.IndexOf(item);
			return index == -1 ? int.MaxValue : index;
		});

		// UpdateListView(GetFilteredData());  // Refresh the list view
	}
	
    private void SortByFavorite()
    {
        SortDataGeneral(item => _favoriteItems.Contains(item) ? 1 : 0);
    }
	
	/// <summary>
	/// Sorts the data source based on item selection history.
	/// </summary>
	/// <remarks>
	/// Items are sorted in descending order based on their selection frequency.
	/// </remarks>
	private void SortByHistory()
	{
		SortDataGeneral(item => 
		{
			_selectionHistory.TryGetValue(item, out int frequency);
			return frequency;
		});		
	}	
	
	private void UpdateDataByHistory()
	{
		_noFilteredData = _noFilteredData.OrderByDescending(item =>
		{
			_selectionHistory.TryGetValue(item, out int frequency);
			return frequency;
		}).ToList();
	}
	
	private void UpdateSelectionOrder(object selectedItem)
	{
		_selectionOrder.Remove(selectedItem);  // Remove the item if it's already in the list
		_selectionOrder.Add(selectedItem);  // Add the item to the end of the list
	}
	
	#region-- Favorite
	void InitializeFavoriteIcon(object itemIdentifier, VisualElement favoriteIcon) {
		Texture2D iconTexture;

		if (_favoriteItems.Contains(itemIdentifier))
        {
			iconTexture = favoriteIconOn;
		} 
		else 
		{
			iconTexture = favoriteIconOff;
		}

		favoriteIcon.style.backgroundImage = Background.FromTexture2D(iconTexture);
	}

void OnFavoriteIconClicked(object itemIdentifier, VisualElement favoriteIcon)
{
    Texture2D iconTexture;

    // Toggle favorite status
	if (_favoriteItems.Contains(itemIdentifier))
    {
		_favoriteItems.Remove(itemIdentifier);
		iconTexture = favoriteIconOff;		
    }
    else
    {
		_favoriteItems.Add(itemIdentifier);
		iconTexture = favoriteIconOn;		
    }

    favoriteIcon.style.backgroundImage = Background.FromTexture2D(iconTexture);
}

	private void UpdateFavoriteIconCallback(VisualElement favoriteIcon, object itemObject) 
	{
		favoriteIcon.userData = itemObject;  // Store object reference in the element's userData
		favoriteIcon.UnregisterCallback<ClickEvent>(GeneralFavoriteIconClicked);
		favoriteIcon.RegisterCallback<ClickEvent>(GeneralFavoriteIconClicked);
	}

	private void GeneralFavoriteIconClicked(ClickEvent evt)
	{
		var favoriteIcon = evt.target as VisualElement;
		var itemObject = favoriteIcon.userData;  // Retrieve object reference from userData
		OnFavoriteIconClicked(itemObject, favoriteIcon);
	}
	#endregion	

	private void InitializeUI()
	{
		InitializeUIElements();
		ConfigureFavoriteSortToggleBehavior();
		ConfigureHistorySortToggleBehavior();
		ConfigureDefaultValues();		
	}
	private void SortDataGeneral(Func<object, object> sortingFunc)
	{
		if (_noFilteredData == null)
		{
			return;
		}

		_noFilteredData = _noFilteredData.OrderBy(sortingFunc).ToList();
		UpdateListView(GetFilteredData());
	}
	
	private List<object> SortFilteredData(List<object> filteredList)
	{
		if (_favoriteSortToggle.value)
		{
			filteredList = filteredList.OrderByDescending(item => 
			{
				bool isFavorite = _favoriteItems.Contains(item);
				return isFavorite;
			}).ToList();
		}

		if (_historySortToggle.value)
		{
			filteredList = filteredList.OrderByDescending(item => 
			{
				_selectionHistory.TryGetValue(item, out int frequency);
				return frequency;
			}).ToList();
		}

		return filteredList;
	}
	
	private void ConfigureFavoriteSortToggleBehavior()
	{
		// ... Move all favorite related logic here.
		_favoriteSortToggle.RegisterValueChangedCallback(evt => { 
			currentSortType = evt.newValue ? SortType.ByFavorite : SortType.Alphabetical;
			SortData();
		});		
	}
	
	// private void InitializeSortByHistoryLogic()
	private void ConfigureHistorySortToggleBehavior()
	{
		_historySortToggle.RegisterValueChangedCallback(evt => { 
			currentSortType = evt.newValue ? SortType.ByHistory : SortType.Alphabetical;
			SortData();
		});		
	}
	
	private string ProcessPrefix(string input)
	{
		return RemovePrefixString(_prefixToRemove, input).ToLower();
	}
	
    void CheckAndCreateFile(string filePath)
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
}

#region-- TODO to put in separate files. We keep all here for now to easy upload to chatGPT.
#endregion