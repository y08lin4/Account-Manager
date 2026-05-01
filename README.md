# AccountManager

Windows 本地账号管理器，使用 C# WPF + SQLite。

## 功能

- 首次启动设置主密码、密码提示词、密码保护问题/答案
- 启动必须输入主密码解锁
- 忘记主密码时可通过保护问题重置主密码
- 账号字段：邮箱、密码、2FA、分类、标签、备注
- TXT / 粘贴批量导入：`邮箱--密码--2FA`
- 支持选择多个 TXT、拖拽多个 TXT 到主窗口、导入前预览
- 支持快捷导入：粘贴多行账号，填写分类和标签后直接入库
- 搜索大小写不敏感
- 双向部分匹配：数据库邮箱 `bqmlutxsw20435@outlook.jp` 可以被 `codex-bqmlutxsw20435@outlook.jp-plus` 命中
- 命中内容高亮：黄色表示搜索词在字段内，绿色表示字段整体被搜索词包含
- 账号列表右键复制邮箱/密码/2FA/整行
- 密码和 2FA 默认隐藏，可手动显示
- 复制后默认 30 秒自动清空剪贴板
- 导出 TXT / CSV
- 一键创建完整加密数据库备份，首次备份选择目录，后续直接备份到该目录
- 导入备份后会重启，必须输入该备份对应的主密码
- 本地 API：可在软件内输入主密码查看、复制、新建或删除 API Token
- 小窗/大窗双布局：小窗用于日常搜索复制，大窗用于批量整理和管理
- API 开关：可关闭本地 API 监听；界面锁定后 API 可按开关继续服务
- 自动锁定：支持关闭、5 分钟、10 分钟、30 分钟
- 备份历史：查看、打开、删除或恢复默认备份目录中的备份
- 导入重复预览：导入前显示新增、覆盖、跳过、数据库重复、文本内重复和错误数量
- 数据库检查：检查 SQLite quick_check、表结构、安全配置、账号解密和备份数量

## 数据目录

同一套代码支持两个发布形态：

| 版本 | 数据位置 |
|---|---|
| 绿色便携版 | exe 同目录下的 `data/accounts.db` |
| 安装版 | `%APPDATA%/AccountManager/accounts.db` |

## 构建

开发构建：

```powershell
dotnet build
```

绿色便携版：

```powershell
powershell -ExecutionPolicy Bypass -File scripts\publish-portable.ps1
```

输出：

```text
dist/portable/AccountManager.exe
dist/AccountManager_Portable.zip
```

安装版应用文件：

```powershell
powershell -ExecutionPolicy Bypass -File scripts\publish-installed.ps1
```

输出：

```text
dist/installed/app/AccountManager.exe
```

如需生成安装包，先安装 Inno Setup 6，然后重新运行：

```powershell
powershell -ExecutionPolicy Bypass -File scripts\publish-installed.ps1
```

输出：

```text
dist/installed/AccountManager_Setup.exe
```

GitHub Actions 会在推送 main、手动运行或发布 Release 时自动构建：

```text
AccountManager_Portable.zip
AccountManager_Installed_App.zip
AccountManager_Setup.exe
```

## 备份说明

“备份”会复制完整 SQLite 数据库。账号密码、2FA、备注字段以本地加密形式存储，备份里也保留主密码配置、提示词、保护问题、API Token 和软件设置。

首次点击“备份”会选择默认备份目录；之后再次点击“备份”会直接在该目录创建：

```text
AccountManager_Backup_yyyyMMdd_HHmmss.db
```

默认备份目录可以在“设置 → 路径”里修改。

导入备份后程序会重启；恢复后的数据库需要输入该备份创建时对应的主密码才能使用。
