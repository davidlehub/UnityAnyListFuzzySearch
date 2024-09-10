1. Install: com.unity.nuget.newtonsoft-json
	Package Manager >> Add package from git URL... >> paste this text: "com.unity.nuget.newtonsoft-json", then click Add.
2. Assembly Definition reference:
	Add "DavidD.UnityAnyListFuzzySearch.Editor.asmdef" reference where needed.
	
-----
For Wise-Feline. Testing for adding a Consideration:
- Here is the modification of ShowConsiderationList(), in the file `Assets\NoOpArmy.WiseFeline\Scripts\UtilityAI\Editor\Scripts\BehaviorView.cs`. :

```
private void ShowConsiderationList()
{
	#region (TamModif. Show FuzzySearchWindow UI Window.) |020231016095550
	
	/* No need: It replaced by the FuzzySearchWindow Window
	var menu = new GenericMenu();

	bool isSelected = false;
	foreach (var type in ReflectionUtilities.GetAllDerivedTypes(typeof(ConsiderationBase)))
	{
		menu.AddItem(new GUIContent(type.Name), isSelected, value => CreateConsideration(value), type);
	}

	// Get position of menu on top of target element.
	var menuPosition = new Vector2(_considerationButton.layout.xMax, _considerationButton.layout.yMin);
	menuPosition = _considerationButton.parent.LocalToWorld(menuPosition);
	var menuRect = new Rect(menuPosition, Vector2.zero);
	menu.DropDown(menuRect);
	*/
	

	/// <summary>
	/// *** Setting what to show, as string, for each columns of the FuzzySearch's list view. 
	/// Also, this array order must match with 'ui:Column' in 'FuzzySearchWindow.uxml. For example:
	/// ' ui:Column name="0" ' for the first column, ' ui:Column name="1" ' for the second column, etc.
	/// </summary>
	var fieldExtractors = new Func<object, object>[]
	{
		//- TODO: validating the matching of this array with the 'Columns' (the child items of '<ui:Columns name="Columns">') in FuzzySearchWindow.uxml. Plus a video tutorial.
		
		item => 
		{ 
			return ((Type)item).Name; 
		},
		item =>
		{
			return ((Type)item).ToString();
			// return ((Type)item).Assembly.ToString(); //- For example.
		}
	};
	
	//- Opening a new FuzzySearchWindow window.
	var fuzztSearchWindow = EditorWindow.GetWindow<FuzzySearchWindow>();

	//- Need to communicate with FuzzySearchWindow Window. And a callback with the chosen item from the FuzzySearchWindow list.
	var types = ReflectionUtilities.GetAllDerivedTypes(typeof(ConsiderationBase));
	fuzztSearchWindow.Summon(types, fieldExtractors, chosenItem =>
	{
		CreateConsideration(chosenItem);
	});
	#endregion
}

```


- Icons used - Attribution:
<a href="https://www.flaticon.com/free-icons/gold-star" title="gold star icons">Gold star icons created by chehuna - Flaticon</a>

<a href="https://www.flaticon.com/free-icons/star" title="star icons">Star icons created by Pixel perfect - Flaticon</a>