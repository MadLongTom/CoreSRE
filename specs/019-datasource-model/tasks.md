# Tasks — SPEC-200~203: SRE 数据源集成

> 覆盖 SPEC-200（领域 + CRUD）、SPEC-201（查询抽象 + Prometheus/Loki）、SPEC-202（Agent 绑定 + AIFunction）、SPEC-203（Tracing/Alerting）

---

## Phase 1 — Domain 层（SPEC-200）

- [X] T001 创建 `DataSourceCategory` 枚举 `Backend/CoreSRE.Domain/Enums/DataSourceCategory.cs`
- [X] T002 创建 `DataSourceProduct` 枚举 `Backend/CoreSRE.Domain/Enums/DataSourceProduct.cs`
- [X] T003 创建 `DataSourceStatus` 枚举 `Backend/CoreSRE.Domain/Enums/DataSourceStatus.cs`
- [X] T004 创建 `DataSourceConnectionVO` 值对象 `Backend/CoreSRE.Domain/ValueObjects/DataSourceConnectionVO.cs`
- [X] T005 创建 `QueryConfigVO` 值对象 `Backend/CoreSRE.Domain/ValueObjects/QueryConfigVO.cs`
- [X] T006 创建 `DataSourceHealthVO` 值对象 `Backend/CoreSRE.Domain/ValueObjects/DataSourceHealthVO.cs`
- [X] T007 创建 `DataSourceMetadataVO` 值对象 `Backend/CoreSRE.Domain/ValueObjects/DataSourceMetadataVO.cs`
- [X] T008 创建 `DataSourceRegistration` 实体 `Backend/CoreSRE.Domain/Entities/DataSourceRegistration.cs`
- [X] T009 创建 `IDataSourceRegistrationRepository` 接口 `Backend/CoreSRE.Domain/Interfaces/IDataSourceRegistrationRepository.cs`

## Phase 2 — Application 层 CRUD（SPEC-200）

- [X] T010 创建 DTOs `Backend/CoreSRE.Application/DataSources/DTOs/`
- [X] T011 创建 `RegisterDataSourceCommand` + Handler + Validator `Backend/CoreSRE.Application/DataSources/Commands/RegisterDataSource/`
- [X] T012 创建 `UpdateDataSourceCommand` + Handler + Validator `Backend/CoreSRE.Application/DataSources/Commands/UpdateDataSource/`
- [X] T013 创建 `DeleteDataSourceCommand` + Handler `Backend/CoreSRE.Application/DataSources/Commands/DeleteDataSource/`
- [X] T014 创建 `GetDataSourcesQuery` + Handler `Backend/CoreSRE.Application/DataSources/Queries/GetDataSources/`
- [X] T015 创建 `GetDataSourceByIdQuery` + Handler `Backend/CoreSRE.Application/DataSources/Queries/GetDataSourceById/`

## Phase 3 — Infrastructure 层（SPEC-200）

- [X] T016 创建 `DataSourceRegistrationConfiguration` EF Core 配置 `Backend/CoreSRE.Infrastructure/Persistence/Configurations/DataSourceRegistrationConfiguration.cs`
- [X] T017 创建 `DataSourceRegistrationRepository` `Backend/CoreSRE.Infrastructure/Persistence/DataSourceRegistrationRepository.cs`
- [X] T018 注册 DbSet + DI `Backend/CoreSRE.Infrastructure/Persistence/AppDbContext.cs` + `Backend/CoreSRE.Infrastructure/DependencyInjection.cs`
- [X] T019 创建 EF Core Migration

## Phase 4 — API 端点（SPEC-200）

- [X] T020 创建 `DataSourceEndpoints` `Backend/CoreSRE/Endpoints/DataSourceEndpoints.cs`
- [X] T021 注册端点 `Backend/CoreSRE/Program.cs`

## Phase 5 — 查询抽象层（SPEC-201）

- [X] T022 创建统一查询/响应 VO `Backend/CoreSRE.Domain/ValueObjects/DataSourceQueryVO.cs` + `DataSourceResultVO.cs`
- [X] T023 创建 `IDataSourceQuerier` + `IDataSourceQuerierFactory` 接口 `Backend/CoreSRE.Application/Interfaces/`
- [X] T024 创建 `QueryDataSourceCommand` + Handler `Backend/CoreSRE.Application/DataSources/Commands/QueryDataSource/`
- [X] T025 创建 `TestDataSourceConnectionCommand` + Handler `Backend/CoreSRE.Application/DataSources/Commands/TestConnection/`
- [X] T026 创建 `DiscoverMetadataCommand` + Handler `Backend/CoreSRE.Application/DataSources/Commands/DiscoverMetadata/`

## Phase 6 — Prometheus / Loki Querier（SPEC-201）

- [X] T027 创建 `PrometheusQuerier` `Backend/CoreSRE.Infrastructure/Services/DataSources/PrometheusQuerier.cs`
- [X] T028 创建 `LokiQuerier` `Backend/CoreSRE.Infrastructure/Services/DataSources/LokiQuerier.cs`
- [X] T029 创建 `DataSourceQuerierFactory` `Backend/CoreSRE.Infrastructure/Services/DataSources/DataSourceQuerierFactory.cs`
- [X] T030 注册 Querier DI + 查询端点 

## Phase 7 — Agent 绑定（SPEC-202）

- [X] T031 创建 `DataSourceRefVO` `Backend/CoreSRE.Domain/ValueObjects/DataSourceRefVO.cs`
- [X] T032 扩展 `LlmConfigVO` 添加 `DataSourceRefs`
- [X] T033 创建 `IDataSourceFunctionFactory` 接口 + 实现
- [X] T034 更新 `AgentResolverService` 集成 DataSource AIFunction
- [X] T035 EF Core 迁移（LlmConfig JSONB + DataSourceRegistrations 表）

## Phase 8 — Tracing / Alerting Querier（SPEC-203）

- [X] T036 创建 `JaegerQuerier` `Backend/CoreSRE.Infrastructure/Services/DataSources/JaegerQuerier.cs`
- [X] T037 创建 `AlertmanagerQuerier` `Backend/CoreSRE.Infrastructure/Services/DataSources/AlertmanagerQuerier.cs`
- [X] T038 注册 Querier DI

## Phase 9 — 构建与验证

- [X] T039 dotnet build 验证（0 errors, 6 warnings）
- [X] T040 dotnet test 验证（370 pass, 5 pre-existing failures unrelated to this feature）
