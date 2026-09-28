Power Station Free Tileset（整理版）

来源：https://free-game-assets.itch.io/power-station-free-tileset-pixel-art
授权：按 CraftPix 授权条款使用（https://craftpix.net/file-licenses/），商用前请确认。素材文件本身不能二次分发。

整理内容
Tiles/Tileset.png        64 块 32x32 瓦片，已自动切成 Tileset_00 到 Tileset_63（左上角开始，从左到右、从上到下）
                         最下面两行（Tileset_48 到 Tileset_63）是科技面板，适合太空站墙面和地板
Backgrounds/Day, Night   5 层视差背景（1 最远，5 最近），Overlay.png 为叠加层
Objects/Tubes            管道
Objects/Decoration       控制台、屏幕、储罐、书架、椅子等
Animated/Trap.png        电弧陷阱 4 帧（32x48），自动生成 Trap.anim
Animated/Card.png        钥匙卡旋转 8 帧（24x24），自动生成 Card.anim

没有导入的：高压电塔和电线、钞票、宝箱、单独的 Tile_01 到 Tile_64（和 Tileset.png 内容重复）

导入设置（由 Assets/Resource/Scripts/Editor/PowerStationTilesetImporter.cs 在第一次导入时自动设置）
Pixels Per Unit 32（一块瓦片 = 1 个格子，和场景 Grid 的 1x1 对应）
Filter Mode Point，Compression None，不生成 Mipmap

如果导入设置被改乱了：菜单 Tools > Gravity Game > Power Station Tileset > 重新应用导入设置

画地图：打开 Window > 2D > Tile Palette，新建一个 Palette，把 Tiles/Tileset.png 拖进去即可。
