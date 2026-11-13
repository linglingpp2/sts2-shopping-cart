# Shop Shopping Cart 1.0.4

修复游戏更新后购物车无法加载的问题，同时适配 Windows 稳定版 **v0.107.1 / public** 与最新测试版 **v0.113.0 / public-beta**。

- 改用两版均有的商品选择入口，修复旧购买方法被移除导致的初始化崩溃。
- 测试版接入原生商店购买同步器，保留购买消息、库存购买锁和删牌取消流程；稳定版自动使用原有购买封装。
- 保留会员卡优先结算、金额预估、删牌服务和右侧购物车面板。对局结束后停止结算。
- 同一个 DLL 通过稳定版 **51/51**、测试版 **56/56** 项检查，并通过两版独立引擎界面检查。[验证范围](https://github.com/linglingpp2/sts2-shopping-cart/blob/main/docs/COMPATIBILITY-1.0.4.md)。完整游玩、真实手柄和真实多人联机仍待实测。

[创意工坊订阅](https://steamcommunity.com/sharedfiles/filedetails/?id=3812514991)：等待下载后重启游戏。手动安装则退出游戏，将 ZIP 内的 `ShoppingCart` 文件夹复制至游戏 `mods` 目录。订阅版与手动版保留一种；无需 BaseLib 或额外 PCK。

## English

Fixes the mod failing to initialize after the merchant API changes in game v0.113.0. One DLL supports Windows stable **v0.107.1 / public** and beta **v0.113.0 / public-beta**.

The mod intercepts the shared slot-selection method and chooses the version's native purchase entry point. Beta uses `MerchantSynchronizer.PurchaseLocalEntry`, preserving purchase messages, inventory locks and cancellation. Stable uses its legacy wrappers. Membership Card priority, card removal and the cart panel remain available. Checkout stops when the run ends.

The identical DLL passed **51/51 stable** and **56/56 beta** checks and separate engine UI fixtures on both. UI and transport boundaries are replaced in tests; full gameplay, physical controllers and real multiplayer peers remain unverified.

[Subscribe on Steam Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3812514991), wait for the download and restart. For manual installation, exit the game and copy the ZIP's `ShoppingCart` folder into `mods`. Keep either the Workshop or manual copy. No BaseLib or PCK required.

MIT License. [Source and support](https://github.com/linglingpp2/sts2-shopping-cart).
