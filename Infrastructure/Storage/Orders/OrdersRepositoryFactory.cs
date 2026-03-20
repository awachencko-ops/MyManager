namespace Replica
{
    public static class OrdersRepositoryFactory
    {
        public static IOrdersRepository Create(AppSettings settings, string historyFilePath)
        {
            if (settings != null && settings.OrdersStorageBackend == OrdersStorageMode.LanPostgreSql)
                return new PostgreSqlOrdersRepository(settings.LanPostgreSqlConnectionString);

            return new FileSystemOrdersRepository(historyFilePath);
        }

        public static IOrdersRepository CreateFileSystem(string historyFilePath)
        {
            return new FileSystemOrdersRepository(historyFilePath);
        }
    }
}
