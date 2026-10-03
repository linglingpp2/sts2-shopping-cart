# Shop Shopping Cart · 商店购物车

Slay the Spire 2 的购物车 mod：先挑选商店商品，再一次结算，让面板帮你计算花费和余额。

A shopping-cart mod for Slay the Spire 2. Select your purchases first, review the estimated cost, then check out together. [English README](README.en.md)

**当前版本：1.0.3。** [下载发行包](https://github.com/linglingpp2/sts2-shopping-cart/releases/latest) · [更新记录](CHANGELOG.md) · [反馈问题](https://github.com/linglingpp2/sts2-shopping-cart/issues)

## 功能与操作

- **点击加入购物车**：打开商店商品列表后，点击卡牌、遗物、药水或删牌服务即可加入；再次点击取消。选中的商品显示绿色勾选角标。
- **右侧购买面板**：显示预计购买顺序、总花费及结算后余额。金币不足时会提示；面板可以拖动。
- **会员卡优先**：只有会员卡已加入购物车，且结算开始时的金币足够购买它，才会自动先买会员卡，并预估后续商品的折扣。其他商品保持加入顺序；不会自动选择商店里的会员卡。买不起会员卡时保持原顺序。
- **删牌服务**：费用计入购物车，包括适用的会员卡折扣。结算轮到删牌时打开原版选牌界面，确认后继续购买；取消选牌则不收删牌费用，并停止后续购买。此前已经买到的商品不会退款。
- **一键结算**：按面板中的顺序逐件购买。购买失败时停止，未购买的商品留在购物车。结算期间暂停其他商店购买及本地地图操作。
- **直接购买开关**：在面板上关闭点击加入模式，恢复原版即点即买。
- **收起和找回面板**：点击右上角购物车图标展开或收起，右键点击图标将面板恢复到默认位置。图标会显示购物车数量。

关闭商店商品列表会清空购物车。面板位置和点击模式保存在游戏用户目录的 `shopping_cart.cfg` 中。

金额是预估值。已支持会员卡折扣及部分原版遗物的价格、返金效果；遗物和其他 mod 仍可能在购买过程中改变结果。**最终商品价格、效果和支付由原版购买流程决定。** 结算开始后使用固定购买顺序，途中返金不会重新排序。

## 安装与更新

1. 下载 `ShoppingCart-1.0.3.zip`，解压。
2. 退出游戏，将包里的整个 `ShoppingCart` 文件夹复制到游戏目录的 `mods` 文件夹。更新时覆盖同名文件。
3. 启动游戏，打开商店商品列表查看右侧购物车面板。更新 DLL 后需要重新启动游戏。

安装后的目录应为：

```text
<游戏目录>/mods/ShoppingCart/
├── ShoppingCart.dll
└── ShoppingCart.json
```

透明购物车图标已嵌入 DLL；无需额外 PCK 文件，也无需安装 BaseLib。发行包中的 `LICENSE` 与 `README.txt` 用于说明和许可，不需要放入游戏目录。

当前还未发布创意工坊条目。以后测试订阅版时，请先将相同 ID 的手动安装版移出游戏 `mods` 目录，确保实际测试的是订阅版本。

## 兼容性与验证范围

- 当前构建及验证使用 **Windows、游戏版本 0.111.0**；清单最低游戏版本为 0.111.0。
- 50 项自动回归覆盖购买顺序、会员卡折扣、返金、删牌确认和取消、购买失败、并发保护、异常恢复及商店关闭。测试使用真实游戏程序集，但替换了界面和部分同步边界，不等同于真实游戏流程测试。
- 右侧面板还通过了独立引擎界面验证。完整游玩、真实手柄设备及多人联机仍待实测；其他操作系统和游戏版本尚未验证。

游戏更新可能改变内部购买或界面接口。反馈问题时，请附上游戏版本、mod 版本、其他已启用 mod、复现步骤和相关日志。[提交 Issue](https://github.com/linglingpp2/sts2-shopping-cart/issues)

## 从源码构建

需要 **.NET 9 SDK** 和已安装的 Windows 游戏。构建时引用游戏 `data_sts2_windows_x86_64` 目录内的 `sts2.dll`、`GodotSharp.dll` 与 `0Harmony.dll`；仓库和发行包均不包含这些文件，也不捆绑 SDK。

在仓库根目录运行下面的 PowerShell 命令，把 `<游戏目录>` 替换为实际路径：

```powershell
# 仅构建，不修改游戏安装目录
.\build.ps1 -GameDir '<游戏目录>' -SkipInstall

# 运行回归测试
.\test.ps1 -GameDir '<游戏目录>'

# 生成发行 ZIP 和 SHA-256 校验文件，不安装 mod
.\package.ps1 -GameDir '<游戏目录>'
```

如果 `dotnet` 未加入 PATH，可在上述命令中添加 `-DotnetPath '<dotnet.exe 的路径>'`。也可设置 `STS2_GAME_DIR` 环境变量，然后省略 `-GameDir`。不同游戏文件布局可通过 `-GameDataDir '<游戏程序集目录>'` 指定。

构建产物位于 `ShoppingCartMod/bin/Release/`；发行产物位于 `dist/`。省略 `-SkipInstall` 的 `build.ps1` 会将 mod 安装到指定游戏，并先把已有 DLL 和清单备份到本地 `backups/` 目录；替换安装前请退出游戏。

Windows Git Bash 下也可运行 `./build.sh '<游戏目录>' --no-install`。这个入口不表示已支持 Linux 或 macOS 游戏。

## 项目内容

```text
ShoppingCartMod/       mod 项目、清单和原创源码
validation/           自动回归测试与测试边界替身
docs/                 发行说明
build.ps1 / build.sh   构建及可选安装
test.ps1              自动回归入口
package.ps1           发行包生成入口
```

项目通过游戏内置 mod 加载器与 Harmony 补丁接入商店，继续调用原版购买、价格和删牌流程。游戏程序集仅从开发者自己的游戏安装中引用。

## 许可证

本项目原创代码和内嵌购物车图标采用 [MIT License](LICENSE)，版权归 lin。游戏与第三方组件不在本项目许可授权范围内，详见 [第三方说明](THIRD_PARTY_NOTICES.md)。本项目是非官方 mod。
