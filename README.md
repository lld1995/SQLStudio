# SQL Studio - AI SQL Generator

基于AI的SQL生成智能体，支持模块化数据库连接器，具有自动错误重试机制。

## 功能特性

- **模块化数据库连接器**: 动态支持 MySQL、ClickHouse、PostgreSQL、SQL Server
- **AI SQL 生成**: 基于自然语言描述自动生成SQL查询
- **错误自动重试**: SQL执行失败时自动将错误信息反馈给AI重新生成
- **多AI提供商支持**: OpenAI、Azure OpenAI、Ollama

## 项目结构

```
SQLStudio/
├── Core/
│   ├── AI/
│   │   ├── ISqlGeneratorAgent.cs      # SQL生成代理接口
│   │   ├── SqlGeneratorAgent.cs       # SQL生成代理实现
│   │   ├── SqlAgentExecutor.cs        # SQL执行器（含重试机制）
│   │   └── AiServiceFactory.cs        # AI服务工厂
│   ├── Database/
│   │   ├── IDatabaseConnector.cs      # 数据库连接器接口
│   │   ├── BaseDatabaseConnector.cs   # 连接器基类
│   │   ├── DatabaseConnectorFactory.cs# 连接器工厂
│   │   └── Connectors/
│   │       ├── MySqlDatabaseConnector.cs
│   │       ├── ClickHouseDatabaseConnector.cs
│   │       ├── PostgreSqlDatabaseConnector.cs
│   │       └── SqlServerDatabaseConnector.cs
│   └── Services/
│       └── ServiceConfiguration.cs    # 服务配置
├── ViewModels/
│   └── MainViewModel.cs               # 主视图模型
├── MainWindow.axaml                   # 主窗口UI
└── appsettings.json                   # 配置文件
```

## 快速开始

### 1. 配置

编辑 `appsettings.json` 配置AI和数据库连接：

```json
{
  "AI": {
    "Provider": "OpenAI",
    "ApiKey": "your-api-key-here",
    "ModelId": "gpt-4o"
  },
  "DefaultDatabase": {
    "Type": "MySQL",
    "Host": "localhost",
    "Port": 3306,
    "Database": "test",
    "Username": "root",
    "Password": ""
  }
}
```

### 2. 运行

```bash
cd SQLStudio
dotnet run
```

### 3. 使用

1. 在界面中配置数据库连接信息和AI API Key
2. 点击 "Connect" 连接数据库
3. 在 "Natural Language Query" 输入框中输入自然语言描述
4. 点击 "Generate & Execute SQL" 生成并执行SQL

## 核心架构

### 数据库连接器

```csharp
// 创建连接器
var connector = DatabaseConnectorFactory.Create(DatabaseType.MySQL);

// 连接数据库
await connector.ConnectAsync(new DatabaseConnectionConfig
{
    Host = "localhost",
    Port = 3306,
    Database = "mydb",
    Username = "root",
    Password = "password"
});

// 执行查询
var result = await connector.ExecuteQueryAsync("SELECT * FROM users");
```

### AI SQL 生成

```csharp
// 配置AI服务
var aiConfig = new AiServiceConfig
{
    Provider = AiProvider.OpenAI,
    ApiKey = "your-api-key",
    ModelId = "gpt-4o"
};

var sqlGenerator = AiServiceFactory.CreateSqlGenerator(aiConfig);

// 生成SQL
var result = await sqlGenerator.GenerateSqlAsync(new SqlGenerationRequest
{
    UserQuery = "查询最近30天注册的用户",
    Schema = schema,
    DatabaseType = "MySQL"
});
```

### 错误重试机制

```csharp
var executor = new SqlAgentExecutor(sqlGenerator, connector, new SqlAgentOptions
{
    MaxRetries = 3
});

// 订阅事件
executor.OnSqlGenerated += (_, e) => Console.WriteLine($"Generated: {e.Sql}");
executor.OnRetrying += (_, e) => Console.WriteLine($"Retrying due to: {e.ErrorMessage}");

// 执行（自动重试）
var result = await executor.ExecuteAsync("查询所有活跃用户");
```

## 扩展新的数据库连接器

1. 创建新类继承 `BaseDatabaseConnector`
2. 实现抽象方法
3. 在 `DatabaseConnectorFactory` 中注册

```csharp
public class MyDatabaseConnector : BaseDatabaseConnector
{
    public override string DatabaseType => "MyDatabase";
    
    public override async Task ConnectAsync(DatabaseConnectionConfig config, CancellationToken ct)
    {
        // 实现连接逻辑
    }
    
    // 实现其他抽象方法...
}

// 注册
DatabaseConnectorFactory.RegisterConnector(
    DatabaseType.MyDatabase, 
    () => new MyDatabaseConnector()
);
```

## 依赖

- .NET 9.0
- Avalonia UI 11.3
- Microsoft Semantic Kernel
- MySqlConnector
- ClickHouse.Client
- Npgsql
- Microsoft.Data.SqlClient

## License

MIT
