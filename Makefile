# ============================================
# CoreSRE - 项目管理 Makefile
# ============================================

# 变量定义
BACKEND_DIR  = Backend/CoreSRE
FRONTEND_DIR = Frontend
DOTNET       = dotnet
NPM          = npm

# ============================================
# 🚀 常用命令
# ============================================

.PHONY: help
help: ## 显示所有可用命令
	@echo.
	@echo  CoreSRE Project Commands
	@echo  ========================
	@echo.
	@echo  install          - 安装所有依赖 (前端 + 后端)
	@echo  dev              - 一键启动前后端开发环境
	@echo  dev-backend      - 仅启动后端
	@echo  dev-frontend     - 仅启动前端
	@echo  build            - 构建所有项目
	@echo  build-backend    - 构建后端
	@echo  build-frontend   - 构建前端
	@echo  clean            - 清理所有构建产物
	@echo  lint             - 运行前端 lint
	@echo  ui-add           - 添加 shadcn/ui 组件 (用法: make ui-add c=button)
	@echo.

# ============================================
# 📦 依赖安装
# ============================================

.PHONY: install install-backend install-frontend
install: install-backend install-frontend ## 安装所有依赖

install-backend: ## 安装后端依赖 (dotnet restore)
	cd $(BACKEND_DIR) && $(DOTNET) restore CoreSRE.slnx

install-frontend: ## 安装前端依赖 (npm install)
	cd $(FRONTEND_DIR) && $(NPM) install

# ============================================
# 🔨 构建
# ============================================

.PHONY: build build-backend build-frontend
build: build-backend build-frontend ## 构建所有项目

build-backend: ## 构建后端
	cd $(BACKEND_DIR) && $(DOTNET) build CoreSRE.slnx

build-frontend: ## 构建前端
	cd $(FRONTEND_DIR) && $(NPM) run build

# ============================================
# 🏃 开发服务器
# ============================================

.PHONY: dev dev-backend dev-frontend
dev: ## 一键启动前后端（并行）- Windows 请用 powershell -File dev.ps1
	powershell -ExecutionPolicy Bypass -File dev.ps1

dev-backend: ## 启动后端开发服务器
	cd $(BACKEND_DIR) && $(DOTNET) run

dev-frontend: ## 启动前端开发服务器
	cd $(FRONTEND_DIR) && $(NPM) run dev

# ============================================
# 🧹 清理
# ============================================

.PHONY: clean clean-backend clean-frontend
clean: clean-backend clean-frontend ## 清理所有构建产物

clean-backend: ## 清理后端构建产物
	cd $(BACKEND_DIR) && $(DOTNET) clean CoreSRE.slnx

clean-frontend: ## 清理前端构建产物
	cd $(FRONTEND_DIR) && if exist node_modules rmdir /s /q node_modules
	cd $(FRONTEND_DIR) && if exist dist rmdir /s /q dist

# ============================================
# 🔧 工具命令
# ============================================

.PHONY: lint ui-add
lint: ## 运行前端 ESLint
	cd $(FRONTEND_DIR) && $(NPM) run lint

ui-add: ## 添加 shadcn/ui 组件 (用法: make ui-add c=button)
	cd $(FRONTEND_DIR) && npx shadcn@latest add $(c) -y

# ============================================
# 📊 信息
# ============================================

.PHONY: info
info: ## 显示项目环境信息
	@echo --- .NET ---
	$(DOTNET) --version
	@echo --- Node ---
	node --version
	@echo --- NPM ---
	$(NPM) --version
