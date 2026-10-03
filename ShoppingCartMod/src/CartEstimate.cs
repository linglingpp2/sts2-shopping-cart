using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Rooms;

namespace ShoppingCart;

public sealed record CartEstimateInput(MerchantEntry Entry, decimal PriceBeforeRounding, bool IsCard, decimal? AcquiredPriceMultiplier = null);
public sealed record CartEntryEstimate(MerchantEntry Entry, int Cost, int GoldAfterPurchase, bool CanAfford);

/// <summary>A read-only projection in the checkout plan's order. No hypothetical game hooks are dispatched.</summary>
public sealed class CartEstimate
{
    private static readonly FieldInfo? BaseCostField = typeof(MerchantEntry).GetField("_cost", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly ConcurrentDictionary<(Type Type, string Method, Type Base), bool> OverrideCache = new();
    public int TotalCost { get; private init; }
    public int RemainingGold { get; private init; }
    public bool CanAfford { get; private init; }
    public bool HasUncertainty { get; private init; }
    public bool MembershipCardFirst { get; private init; }
    public IReadOnlyList<CartEntryEstimate> Entries { get; private init; } = Array.Empty<CartEntryEstimate>();

    public int CostFor(MerchantEntry entry) => Entries.FirstOrDefault(item => ReferenceEquals(item.Entry, entry))?.Cost ?? entry.Cost;

    public static CartEstimate Calculate(IReadOnlyList<CartEstimateInput> entries, int gold, int goldPerCard, bool hasUncertainty = false, bool membershipCardFirst = false)
    {
        decimal futureDiscount = 1m;
        int remaining = gold;
        int total = 0;
        bool affordable = true;
        var rows = new List<CartEntryEstimate>(entries.Count);
        foreach (var input in entries)
        {
            int cost = (int)(input.PriceBeforeRounding * futureDiscount);
            // The original wrapper checks gold before adding the card (and before LuckyFysh pays out).
            affordable &= remaining >= cost;
            remaining -= cost;
            total += cost;
            if (input.IsCard) remaining += goldPerCard;
            rows.Add(new CartEntryEstimate(input.Entry, cost, remaining, affordable));
            if (input.AcquiredPriceMultiplier is decimal multiplier)
            {
                futureDiscount *= multiplier;
            }
        }
        return new CartEstimate
        {
            TotalCost = total,
            RemainingGold = remaining,
            CanAfford = affordable,
            HasUncertainty = hasUncertainty,
            MembershipCardFirst = membershipCardFirst,
            Entries = rows,
        };
    }

    internal static CartEstimate For(Player? player, IReadOnlyList<MerchantEntry> entries, CartPurchasePlan? fixedPlan = null)
    {
        if (player == null)
        {
            return Calculate(entries.Select(entry => new CartEstimateInput(entry, entry.Cost, false)).ToList(), 0, 0, true);
        }
        var plan = fixedPlan ?? CartPurchasePlan.Create(entries, player.Gold);
        var listeners = player.RunState.IterateHookListeners(null).ToList();
        bool merchant = player.RunState.CurrentRoom is MerchantRoom;
        bool localPlayer = LocalContext.IsMe(player);
        decimal existingDiscount = 1m;
        bool unknownPrice = false;
        bool uncertain = false;
        int goldPerCard = 0;
        decimal goldGainMultiplier = 1m;
        foreach (var model in listeners)
        {
            if (model is Ectoplasm ectoplasm)
            {
                if (ectoplasm.Owner == player) goldGainMultiplier = 0m;
            }
            else if (model is BowlerHat hat)
            {
                if (hat.Owner == player) goldGainMultiplier *= hat.DynamicVars["GoldIncrease"].BaseValue;
            }
            else if (Overrides(model, nameof(AbstractModel.ModifyGoldGained))
                || Overrides(model, nameof(AbstractModel.AfterModifyingGoldGained)))
            {
                uncertain = true;
            }
            uncertain |= model is not DragonFruit && Overrides(model, nameof(AbstractModel.AfterGoldGained));
        }
        foreach (var model in listeners)
        {
            if (model is MembershipCard member)
            {
                if (member.Owner == player && localPlayer) existingDiscount *= member.DynamicVars["Discount"].BaseValue / 100m;
            }
            else if (model is TheCourier courier)
            {
                if (courier.Owner == player) existingDiscount *= 1m - courier.DynamicVars["Discount"].BaseValue / 100m;
            }
            else if (Overrides(model, nameof(AbstractModel.ModifyMerchantPrice)))
            {
                unknownPrice = true;
            }
            if (model is LuckyFysh fysh && fysh.Owner == player)
            {
                goldPerCard += (int)(fysh.DynamicVars.Gold.BaseValue * goldGainMultiplier);
            }
            else if (Overrides(model, nameof(AbstractModel.AfterCardChangedPiles)))
            {
                uncertain = true;
            }
            uncertain |= Overrides(model, nameof(AbstractModel.ShouldAddToDeck))
                || Overrides(model, nameof(AbstractModel.TryModifyCardBeingAddedToDeck))
                || Overrides(model, nameof(AbstractModel.TryModifyCardBeingAddedToDeckLate))
                || Overrides(model, nameof(AbstractModel.AfterPotionProcured))
                || Overrides(model, nameof(AbstractModel.ShouldProcurePotion))
                || (model is not MawBank && Overrides(model, nameof(AbstractModel.AfterItemPurchased)));
        }
        uncertain |= unknownPrice;
        var inputs = new List<CartEstimateInput>(entries.Count);
        foreach (var entry in plan.Entries)
        {
            // Reproduce the two known native price multipliers before the game's final integer truncation.
            // Unknown price modifiers keep the real current price, and are explicitly approximate after future discounts.
            decimal price;
            if (!unknownPrice && BaseCostField?.GetValue(entry) is int baseCost)
            {
                price = merchant ? baseCost * existingDiscount : baseCost;
            }
            else
            {
                price = entry.Cost;
                uncertain |= BaseCostField == null;
            }
            decimal? acquiredPriceMultiplier = merchant && entry is MerchantRelicEntry relicEntry
                ? relicEntry.Model switch
                {
                    MembershipCard newMember when localPlayer => newMember.DynamicVars["Discount"].BaseValue / 100m,
                    TheCourier newCourier => 1m - newCourier.DynamicVars["Discount"].BaseValue / 100m,
                    _ => null,
                } : null;
            if (entry is MerchantRelicEntry { Model: { } relic } && relic is not MembershipCard && relic is not TheCourier && relic is not PotionBelt
                && (Overrides(relic, nameof(RelicModel.AfterObtained), typeof(RelicModel))
                    || Overrides(relic, nameof(AbstractModel.ModifyMerchantPrice))
                    || Overrides(relic, nameof(AbstractModel.AfterCardChangedPiles))
                    || Overrides(relic, nameof(AbstractModel.ModifyGoldGained))))
            {
                uncertain = true;
            }
            inputs.Add(new CartEstimateInput(entry, price, entry is MerchantCardEntry, acquiredPriceMultiplier));
        }
        return Calculate(inputs, player.Gold, goldPerCard, uncertain, plan.MembershipCardFirst);
    }

    private static bool Overrides(AbstractModel model, string method, Type? baseType = null) =>
        OverrideCache.GetOrAdd((model.GetType(), method, baseType ?? typeof(AbstractModel)), key =>
            key.Type.GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .Any(info => info.Name == key.Method && info.DeclaringType != key.Base));
}
