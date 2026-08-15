# 在新克隆中启用 Git hooks

Git 出于安全考虑，不会在 `clone` 后自动启用仓库内的 hooks。因此，每个新克隆都需要执行一次初始化脚本。脚本只修改当前仓库的 `.git/config`，不会修改系统或全局 Git 配置。

初始化会完成三件事：

- 将 `core.hooksPath` 设置为仓库内的 `.githooks`；
- 启用 `user.useConfigOnly`，禁止 Git 猜测或回退到其他邮箱；
- 检查当前克隆使用的是 GitHub `noreply` 邮箱，否则阻止后续提交。

## Windows PowerShell

仓库维护者可以运行：

```powershell
.\scripts\enable-git-hooks.ps1 -NoreplyEmail "91787866+yuweiyang9611@users.noreply.github.com"
```

其他贡献者应替换为自己的 GitHub `noreply` 地址。

## Linux、macOS 或 Git Bash

```bash
./scripts/enable-git-hooks.sh "YOUR_ID+YOUR_USERNAME@users.noreply.github.com"
```

如果当前克隆已经配置了正确的本地 `user.email`，可以省略邮箱参数。

## 验证

```bash
git config --local --get core.hooksPath
git config --local --get user.useConfigOnly
git config --local --get user.email
```

预期依次得到 `.githooks`、`true` 和一个以 `@users.noreply.github.com` 结尾的地址。提交时，[`.githooks/pre-commit`](.githooks/pre-commit) 会再次检查 author 和 committer 邮箱；不符合要求的提交会被拒绝。

不要使用 `git commit --no-verify` 绕过检查。还建议在 GitHub 的邮箱设置中启用隐藏邮箱和阻止暴露邮箱的命令行推送。
