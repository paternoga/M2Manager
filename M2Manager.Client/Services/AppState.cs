using M2Manager.Shared.Dtos;

namespace M2Manager.Client.Services;

/// <summary>
/// Wspólny stan między stronami: lista mieszkań, słowniki i aktualnie wybrane mieszkanie.
/// Dzięki temu przełączenie mieszkania na dashboardzie jest widoczne też na liście zakupów.
/// </summary>
public sealed class AppState(ApiClient api)
{
    private List<PropertyDto>? _properties;
    private List<LookupDto>? _expenseCategories;
    private List<LookupDto>? _shoppingCategories;
    private List<ShopDto>? _shops;
    private List<LookupDto>? _payers;
    private readonly Dictionary<int, List<RoomDto>> _roomsByProperty = [];

    public event Action? Changed;

    public int? SelectedPropertyId { get; private set; }

    public void SelectProperty(int? propertyId)
    {
        if (SelectedPropertyId == propertyId)
        {
            return;
        }

        SelectedPropertyId = propertyId;
        Changed?.Invoke();
    }

    public async Task<List<PropertyDto>> GetPropertiesAsync(bool refresh = false)
    {
        if (refresh || _properties is null)
        {
            _properties = await api.GetPropertiesAsync();

            // Pierwsze mieszkanie wybieramy automatycznie — mało kto ma ich więcej niż dwa.
            if (SelectedPropertyId is null || _properties.All(p => p.Id != SelectedPropertyId))
            {
                SelectedPropertyId = _properties.FirstOrDefault()?.Id;
            }
        }

        return _properties;
    }

    public async Task<List<RoomDto>> GetRoomsAsync(int propertyId, bool refresh = false)
    {
        if (refresh || !_roomsByProperty.TryGetValue(propertyId, out var rooms))
        {
            rooms = await api.GetRoomsAsync(propertyId);
            _roomsByProperty[propertyId] = rooms;
        }

        return rooms;
    }

    public async Task<List<LookupDto>> GetExpenseCategoriesAsync(bool refresh = false) =>
        _expenseCategories = refresh || _expenseCategories is null
            ? await api.GetExpenseCategoriesAsync()
            : _expenseCategories;

    public async Task<List<LookupDto>> GetShoppingCategoriesAsync(bool refresh = false) =>
        _shoppingCategories = refresh || _shoppingCategories is null
            ? await api.GetShoppingCategoriesAsync()
            : _shoppingCategories;

    public async Task<List<ShopDto>> GetShopsAsync(bool refresh = false) =>
        _shops = refresh || _shops is null
            ? await api.GetShopsAsync()
            : _shops;

    public async Task<List<LookupDto>> GetPayersAsync(bool refresh = false) =>
        _payers = refresh || _payers is null
            ? await api.GetPayersAsync()
            : _payers;

    /// <summary>Czyści cache — wołane po wylogowaniu i po zmianach w słownikach.</summary>
    public void Invalidate()
    {
        _properties = null;
        _expenseCategories = null;
        _shoppingCategories = null;
        _shops = null;
        _payers = null;
        _roomsByProperty.Clear();
    }

    public void InvalidateRooms(int propertyId) => _roomsByProperty.Remove(propertyId);
}
