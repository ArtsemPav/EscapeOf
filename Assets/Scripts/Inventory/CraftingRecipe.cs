using UnityEngine;

/// <summary>
/// Defines a crafting rule: combining ingredientA with ingredientB produces result.
/// Create instances via Assets > Create > Inventory > Crafting Recipe.
/// </summary>
[CreateAssetMenu(fileName = "NewRecipe", menuName = "Inventory/Crafting Recipe")]
public class CraftingRecipe : ScriptableObject
{
    public ItemData ingredientA;
    public ItemData ingredientB;
    public ItemData result;
}
