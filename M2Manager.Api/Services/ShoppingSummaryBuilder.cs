using M2Manager.Shared;
using M2Manager.Shared.Dtos;

namespace M2Manager.Api.Services;

/// <summary>Sumy listy zakupów: łącznie, per pomieszczenie, per kategoria, per status + postęp remontu.</summary>
public static class ShoppingSummaryBuilder
{
    public static ShoppingSummaryDto Build(int propertyId, IReadOnlyCollection<ShoppingItemDto> items)
    {
        // Pozycje porzucone nie powinny psuć ani budżetu, ani paska postępu.
        var active = items.Where(i => i.Status != ShoppingStatus.Cancelled).ToList();

        var doneCount = active.Count(i => i.Status is ShoppingStatus.Bought or ShoppingStatus.Installed);

        var progress = active.Count > 0
            ? Math.Round(doneCount * 100m / active.Count, 1, MidpointRounding.AwayFromZero)
            : 0m;

        var totalCost = Sum(active, i => i.TotalCost);
        var plannedBudget = Sum(active, i => i.PlannedBudget);
        var actualCost = Sum(active, i => i.ActualCost);

        return new ShoppingSummaryDto
        {
            PropertyId = propertyId,
            ItemsCount = items.Count,
            DoneCount = doneCount,
            ProgressPercent = progress,
            TotalCost = totalCost,
            PlannedBudget = plannedBudget,
            ActualCost = actualCost,
            BudgetDifference = Round(plannedBudget - actualCost),
            ByRoom = GroupBy(active, i => (i.RoomId, i.RoomName)),
            ByCategory = GroupBy(active, i => (i.ShoppingCategoryId, i.CategoryName ?? "Bez kategorii")),
            ByStatus = GroupBy(items, i => ((int?)i.Status, PolishLabels.For(i.Status))),
            ByPayer = GroupBy(active, i => (i.PayerId, i.PayerName ?? "Nieprzypisane"))
        };
    }

    private static List<ShoppingGroupTotalDto> GroupBy(
        IEnumerable<ShoppingItemDto> items,
        Func<ShoppingItemDto, (int? Id, string Name)> keySelector) =>
        items
            .Select(i => (Key: keySelector(i), Item: i))
            .GroupBy(x => x.Key)
            .Select(g => new ShoppingGroupTotalDto
            {
                Id = g.Key.Id,
                Key = g.Key.Name,
                ItemsCount = g.Count(),
                TotalCost = Sum(g.Select(x => x.Item), i => i.TotalCost),
                PlannedBudget = Sum(g.Select(x => x.Item), i => i.PlannedBudget),
                ActualCost = Sum(g.Select(x => x.Item), i => i.ActualCost)
            })
            .OrderByDescending(g => g.TotalCost)
            .ThenBy(g => g.Key, StringComparer.CurrentCulture)
            .ToList();

    private static decimal Sum(IEnumerable<ShoppingItemDto> items, Func<ShoppingItemDto, decimal?> selector) =>
        Round(items.Sum(i => selector(i) ?? 0m));

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
