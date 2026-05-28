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

    [Tooltip("If true, the slot holding ingredientA is preserved after crafting (item is not consumed).")]
    public bool conserveIngredientA;

    [Tooltip("If true, the slot holding ingredientB is preserved after crafting (item is not consumed).")]
    public bool conserveIngredientB;
}
