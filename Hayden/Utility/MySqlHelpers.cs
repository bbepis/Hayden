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

		public void Dispose()
		{
			for (int i = 0; i < PoolSize; i++)
			{
				var connection = Connections.Take();

				connection.Dispose();
			}
		}
	}

	public class ChainedQuery : IDisposable
	{
		public MySqlCommand Command { get; }
		protected bool Reuse { get; }

		public ChainedQuery(MySqlCommand command, bool reuse = false)
		{
			Command = command;
			Reuse = reuse;
		}

		public ChainedQuery SetParam(string parameter, object value)
		{
			if (Command.Parameters.Contains(parameter))
			{
				Command.Parameters[parameter].Value = value;
			}
			else
			{
				Command.Parameters.AddWithValue(parameter, value);
			}

			return this;
		}

		public async Task<IList<T>> ExecuteScalarListAsync<T>(int ordinal = 0)
		{
			try
			{
				List<T> objects = new List<T>();

				await foreach (var row in ExecuteRowsAsync())
				{
					objects.Add((T)row[0]);
				}

				return objects;
			}
			finally
			{
				if (!Reuse)
					Command.Dispose();
			}
		}

		public async Task<DataTable> ExecuteTableAsync()
		{
			try
			{
				await using var reader = await Command.ExecuteReaderAsync();

				DataTable table = new DataTable();

				table.Load(reader);

				return table;
			}
			finally
			{
				if (!Reuse)
					Command.Dispose();
			}
		}

		public async IAsyncEnumerable<IDataRecord> ExecuteRowsAsync()
		{
			try
			{
				await using var reader = await Command.ExecuteReaderAsync();

				while (await reader.ReadAsync())
				{
					yield return reader;
				}
			}
			finally
			{
				if (!Reuse)
					Command.Dispose();
			}
		}

		public async Task ExecuteNonQueryAsync()
		{
			try
			{
				await Command.ExecuteNonQueryAsync();
			}
			finally
			{
				if (!Reuse)
					Command.Dispose();
			}
		}

		public void Dispose()
		{
			Command.Dispose();
		}
	}

	public static class MySqlExtensions
	{
		public static ChainedQuery CreateQuery(this MySqlConnection connection, string sql, bool reuse = false)
		{
			return new ChainedQuery(new MySqlCommand(sql, connection), reuse);
		}
		
		public static T GetValue<T>(this IDataRecord row, string column)
		{
			object value = row[column];

			if (value == null || value == DBNull.Value)
				return default;

			return (T)value;
		}
		
		public static T GetValue<T>(this DataRow row, string column)
		{
			object value = row[column];

			if (value == null || value == DBNull.Value)
				return default;

			return (T)value;
		}
	}
}