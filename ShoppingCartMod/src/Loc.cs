using Godot;

namespace ShoppingCart;

/// <summary>购物车面板的双语文案（按游戏当前语言选择）。</summary>
public static class Loc
{
    public static bool Chinese => TranslationServer.GetLocale().StartsWith("zh");

    public static string T(string zh, string en) => Chinese ? zh : en;

    public static string Title => T("购物车", "Cart");
    public static string EmptyHint => T("点击商品加入购物车", "Click items to add them to the cart");
    public static string DirectHint => T("当前点击商品会直接购买", "Items are purchased immediately in this mode.");
    public static string Total => T("合计", "Total");
    public static string EstimatedTotal => T("预计合计", "Estimated total");
    public static string EstimatedRemaining => T("预计余下", "estimated left");
    public static string EstimatedPrice => T("此商品的预计结算价格", "Estimated checkout price for this item");
    public static string Gold => T("金币", "Gold");
    public static string Checkout => T("一键结算", "Checkout");
    public static string CheckingOut => T("结算中...", "Checking out...");
    public static string Clear => T("清空", "Clear");
    public static string Remove => T("从购物车移除", "Remove from cart");
    public static string CardRemoval => T("删牌服务", "Card removal");
    public static string CardRemovalHint => T("结算时选择要移除的卡牌；取消选牌会暂停结算，保留此服务与后续商品", "Choose a card to remove when checkout reaches this service. Canceling stops checkout and keeps the service and remaining items.");
    public static string ModeCart => T("点击模式：加入购物车", "Click mode: add to cart");
    public static string ModeDirect => T("点击模式：直接购买", "Click mode: buy directly");
    public static string DragHint => T("拖动这里移动", "Drag to move");
    public static string AffordWarning => T("金币不足，结算会停在缺钱的商品上", "Not enough gold; checkout stops at the first unaffordable item");
    public static string EstimateHint => T("按选择顺序结算，预计金额已计入已知折扣和返金", "Purchases follow your selection order; known discounts and refunds are included.");
    public static string MembershipCardHint => T("先买会员卡享受折扣，其余商品按选择顺序结算", "Buy the Membership Card first for its discount; other items keep their selection order.");
    public static string MembershipCardUncertainHint => T("先买会员卡享受折扣；商品效果可能改变实际花费", "Buy the Membership Card first; item effects may change the final cost.");
    public static string UncertainEstimateHint => T("按选择顺序结算；商品效果可能改变实际花费", "Purchases follow your selection order; item effects may change the final cost.");
    public static string OpenCartHint => T("左键展开或收起购物车，右键重置面板位置", "Left-click to show or hide the cart; right-click to reset its position.");
    public static string BlockedByTravelHint => T("正在离店或已选择下一房间，当前无法结算购物车", "You are leaving or have selected the next room. Cart checkout is unavailable while travel or a map vote is pending.");
}
