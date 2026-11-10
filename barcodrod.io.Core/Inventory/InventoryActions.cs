namespace barcodrod.io.Core.Inventory;

/// <summary>Friendly action names; persisted action keys remain stable for existing inventories and workbooks.</summary>
public static class InventoryActions
{
    public static string Name(StockAction action) => action switch
    {
        StockAction.Receive => "Add quantity",
        StockAction.Issue => "Remove quantity",
        StockAction.SetCount => "Set total quantity",
        StockAction.Reserve => "Reserve quantity",
        StockAction.Release => "Release reservation",
        StockAction.FulfillReservation => "Use reserved quantity",
        StockAction.Transfer => "Move location",
        _ => action.ToString()
    };

    public static string Name(string action) => Enum.TryParse<StockAction>(action, out var value) ? Name(value) : action;
}
