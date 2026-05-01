# AccountManager 本地 API

AccountManager 启动并解锁后，会在本机开启一个轻量 HTTP API，用来给脚本或其它本地工具读取、写入账号数据。

## 基本信息

- 地址：`http://127.0.0.1:17878`
- 版本前缀：`/api/v1`
- 监听范围：只监听 `127.0.0.1`，不对外网开放
- 启动时机：软件解锁后自动启动
- 锁定/退出后：API 不可用
- 数据权限：API 读写的是当前已解锁数据库

## Token

除健康检查外，其它接口都需要 Token。

Token 文件位置：

- 绿色版：`软件目录\data\api-token.txt`
- 安装版：`%APPDATA%\AccountManager\api-token.txt`

请求头二选一：

```http
Authorization: Bearer am_xxx
```

或：

```http
X-AccountManager-Token: am_xxx
```

PowerShell 示例：

```powershell
$token = (Get-Content ".\data\api-token.txt" -Raw).Trim()
$headers = @{ Authorization = "Bearer $token" }
Invoke-RestMethod "http://127.0.0.1:17878/api/v1/accounts" -Headers $headers
```

## 响应格式

成功：

```json
{
  "ok": true,
  "data": {}
}
```

失败：

```json
{
  "ok": false,
  "error": "错误原因"
}
```

常见状态码：

- `200`：成功
- `201`：已创建
- `400`：请求参数或 JSON 错误
- `401`：Token 错误或缺失
- `404`：账号或接口不存在
- `423`：软件未解锁

## 接口

### 健康检查

不需要 Token。

```http
GET /api/v1/health
```

响应：

```json
{
  "ok": true,
  "data": {
    "name": "AccountManager",
    "api": "v1",
    "unlocked": true,
    "baseUrl": "http://127.0.0.1:17878"
  }
}
```

### 查询账号列表

```http
GET /api/v1/accounts?query=&category=&tag=&includeSecrets=false
```

参数：

- `query`：搜索关键词，支持大小写不敏感和部分包含匹配
- `category`：分类过滤
- `tag`：标签过滤
- `includeSecrets`：是否返回 `password` 和 `twoFa`，默认不返回

示例：

```powershell
Invoke-RestMethod "http://127.0.0.1:17878/api/v1/accounts?query=outlook&tag=JP&includeSecrets=true" -Headers $headers
```

### 查询单个账号

```http
GET /api/v1/accounts/{id}?includeSecrets=true
```

示例：

```powershell
Invoke-RestMethod "http://127.0.0.1:17878/api/v1/accounts/1?includeSecrets=true" -Headers $headers
```

### 新增账号

```http
POST /api/v1/accounts
Content-Type: application/json
```

请求：

```json
{
  "email": "WillisBrandy7878@outlook.com",
  "password": "oq4#ZI1GGHGfUF",
  "twoFa": "KJNR5D5MUWMXKDLD66NJXZ5R2RAYWEE4",
  "category": "API",
  "tags": "JP",
  "remark": ""
}
```

PowerShell：

```powershell
$body = @{
  email = "WillisBrandy7878@outlook.com"
  password = "oq4#ZI1GGHGfUF"
  twoFa = "KJNR5D5MUWMXKDLD66NJXZ5R2RAYWEE4"
  category = "API"
  tags = "JP"
  remark = ""
} | ConvertTo-Json

Invoke-RestMethod "http://127.0.0.1:17878/api/v1/accounts" -Method Post -Headers $headers -ContentType "application/json" -Body $body
```

### 修改账号

```http
PATCH /api/v1/accounts/{id}
Content-Type: application/json
```

只传需要修改的字段。传空字符串表示清空；不传表示保持原值。

请求：

```json
{
  "category": "API",
  "tags": "JP, plus",
  "remark": "已更新"
}
```

### 删除账号

```http
DELETE /api/v1/accounts/{id}
```

示例：

```powershell
Invoke-RestMethod "http://127.0.0.1:17878/api/v1/accounts/1" -Method Delete -Headers $headers
```

### 批量导入

```http
POST /api/v1/import
Content-Type: application/json
```

请求：

```json
{
  "category": "API",
  "tags": "JP",
  "duplicateMode": "skip",
  "text": "WillisBrandy7878@outlook.com--oq4#ZI1GGHGfUF--KJNR5D5MUWMXKDLD66NJXZ5R2RAYWEE4"
}
```

`duplicateMode` 可选：

- `skip`：重复邮箱跳过，默认
- `overwrite`：重复邮箱覆盖
- `keepBoth`：重复邮箱也保留

响应：

```json
{
  "ok": true,
  "data": {
    "totalLines": 100,
    "parsed": 100,
    "inserted": 98,
    "updated": 0,
    "skippedDuplicates": 2,
    "errors": []
  }
}
```

## 字段说明

账号对象：

```json
{
  "id": 1,
  "email": "WillisBrandy7878@outlook.com",
  "password": "includeSecrets=true 时返回",
  "twoFa": "includeSecrets=true 时返回",
  "category": "API",
  "tags": "JP",
  "remark": "",
  "matchField": "邮箱",
  "matchValue": "WillisBrandy7878@outlook.com",
  "createdAt": "2026-05-02T05:20:00.0000000+08:00",
  "updatedAt": "2026-05-02T05:20:00.0000000+08:00"
}
```

注意：

- `twoFa` 返回的是 2FA 密钥，不是 6 位动态验证码。
- 列表接口默认不返回 `password` 和 `twoFa`，需要显式加 `includeSecrets=true`。
- API 写入后，软件界面会自动刷新当前列表。
