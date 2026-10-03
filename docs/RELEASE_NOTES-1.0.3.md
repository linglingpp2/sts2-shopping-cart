# Shop Shopping Cart 1.0.3

商店购物车：先选择卡牌、遗物、药水和删牌服务，再查看预计花费并一次结算。

## 本次更新

- **删牌加入购物车**：点击删牌服务可以选择或取消，费用计入总额，并支持会员卡折扣。
- **保留原版选牌流程**：结算轮到删牌时打开选牌界面；确认后继续购买，取消则不收删牌费用并停止后续购买。此前已完成的购买不会退款。
- **会员卡优先结算**：已选中的会员卡在结算开始时可支付，便先买会员卡；其余商品保持加入顺序。未选会员卡不会自动加入。
- 提供右侧可拖动购买面板、余额预估、购物车图标及直接购买开关。

## 安装

也可通过 [Steam 创意工坊](https://steamcommunity.com/sharedfiles/filedetails/?id=3812514991) 订阅，等待下载后重新启动游戏。当前工坊版本面向 `public-beta` 分支。使用订阅版前，请将相同 ID 的手动版移出游戏 `mods` 目录。

下载并解压 `ShoppingCart-1.0.3.zip`。退出游戏后，将包中的 `ShoppingCart` 文件夹复制到 `<游戏目录>/mods/`，然后重新启动游戏。更新时覆盖同名 DLL 和清单。

安装目录应包含 `mods/ShoppingCart/ShoppingCart.dll` 和 `mods/ShoppingCart/ShoppingCart.json`。无需 PCK 或 BaseLib。

## 验证范围

在 Windows、`public-beta` 分支、游戏 0.111.0 下完成编译；实际从 Steam 下载的 DLL 已通过 50 项独立回归，右侧面板经过独立引擎界面验证。自动测试替换了界面和部分同步边界，完整游玩、真实手柄及多人联机仍待实测，其他平台、分支和游戏版本尚未验证。

购物车金额为预估，最终价格和效果由原版购买流程决定。购买失败会停止后续购买，已购买内容不退款。

## English

Version 1.0.3 adds the card-removal service to the shopping cart and includes its cost in estimates, including applicable Membership Card discounts. Checkout opens the game's card-selection screen when removal is reached. Confirming resumes checkout; cancelling charges no removal fee and stops later purchases without refunding earlier purchases.

An already selected, initially affordable Membership Card is purchased first. All other items retain their selection order. The cart does not add an unselected Membership Card.

Exit the game, extract the ZIP, and copy its `ShoppingCart` folder into the game's `mods` folder. Restart after installing or updating. No PCK file or BaseLib is required.

Alternatively, [subscribe on Steam Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3812514991), wait for the download, and restart. The current Workshop release targets `public-beta`. Move the same-ID manual copy outside the game's `mods` folder before using the subscription.

Built on Windows with game 0.111.0 on `public-beta`. The actual Steam-downloaded DLL passed 50 isolated regression checks and a separate engine UI check. These checks replace UI and some synchronization boundaries; full gameplay, physical controllers, multiplayer, other game branches and versions, and other platforms remain unverified.

MIT License, copyright (c) 2026 lin. [Source and documentation](https://github.com/linglingpp2/sts2-shopping-cart).
