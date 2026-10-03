using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Models.Relics;

namespace ShoppingCart;

/// <summary>The same stable purchase order is used for estimates and checkout.</summary>
public sealed record CartPurchasePlan(IReadOnlyList<MerchantEntry> Entries, bool MembershipCardFirst)
{
    public static CartPurchasePlan Create(IReadOnlyList<MerchantEntry> selectedEntries, int currentGold)
    {
        var entries = selectedEntries.ToList();
        int memberIndex = entries.FindIndex(entry => entry is MerchantRelicEntry { Model: MembershipCard }
            && entry.IsStocked && entry.Cost <= currentGold);
        if (memberIndex < 0) return new CartPurchasePlan(entries, false);

        if (memberIndex > 0)
        {
            var membershipCard = entries[memberIndex];
            entries.RemoveAt(memberIndex);
            entries.Insert(0, membershipCard);
        }
        return new CartPurchasePlan(entries, true);
    }
}
