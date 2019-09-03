using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using MySql.Data.MySqlClient;
using Nito.AsyncEx;

namespace Hayden
{
	public class MySqlConnectionPool : IDisposable
	{
		public AsyncCollection<MySqlConnection> Connections { get; }

		protected string ConnectionString { get; }

		protected int PoolSize { get; }

		public MySqlConnectionPool(string connectionString, int poolSize)
		{
			PoolSize = poolSize;
			ConnectionString = connectionString;

			Connections = new AsyncCollection<MySqlConnection>(poolSize);

			for (int i = 0; i < poolSize; i++)
			{
				var connection = new MySqlConnection(connectionString);

				connection.Open();

				Connections.Add(connection);
			}
		}

		public async Task<PoolObject<MySqlConnection>> RentConnectionAsync()
		{
			return new PoolObject<MySqlConnection>(await Connections.TakeAsync(), obj =>
			{
				if (obj.State != ConnectionState.Open)
				{
					Program.Log("Reviving SQL connection");
					obj.Open();
				}

				Connections.Add(obj);
			});
		}

		public PoolObject<MySqlConnection> RentConnection()
		{
			return new PoolObject<MySqlConnection>(Connections.Take(), obj =>
			{
				if (obj.State != ConnectionState.Open)
					throw new Exception("MySqlConnection state has broken");

				Connections.Add(obj);
			});
		}

		private void ReleaseUnmanagedResources()
		{
			// TODO release unmanaged resources here
		}

		public void Dispose()
		{
			ReleaseUnmanagedResources();
			GC.SuppressFinalize(this);
		}

		~MySqlConnectionPool() {
			ReleaseUnmanagedResources();
		}
	}

	public static class MySqlExtensions
	{
		//public static async Task<IEnumerable<DataRow>> ExecuteReader(this MySqlConnection connection, string sqlQuery)
		//{
			
		//	using (var command = new MySqlCommand(sqlQuery, connection))
		//	using (var reader = await command.ExecuteReaderAsync())
		//	{
		//		object[] rowObjects = new object[reader.FieldCount];

		//		var table = reader.GetSchemaTable();

		//		while (await reader.ReadAsync())
		//		{
		//			var row = table.NewRow();

		//			reader.GetValues(row.ItemArray);

		//			yield return row;
		//		}
				

		//		((IDataRecord)reader).
		//	}
		//}

		public static async Task<IList<T>> ExecuteOrdinalList<T>(this MySqlConnection connection, string sqlQuery, int ordinal = 0)
		{
			using (var command = new MySqlCommand(sqlQuery, connection))
			using (var reader = await command.ExecuteReaderAsync())
			{
				List<T> objects = new List<T>();

				while (await reader.ReadAsync())
				{
					objects.Add((T)reader[0]);
				}

				return objects;
			}
		}
	}
}