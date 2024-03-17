using System.Collections.Generic;

namespace Hayden;
internal class MultiDictionary<TKey, TValue>
{
	private Dictionary<TKey, List<TValue>> MultiValueDictionary { get; } = new();
	private Dictionary<TKey, TValue> SingleValueDictionary { get; } = new();

	public void Add(TKey key, TValue value)
	{
		if (MultiValueDictionary.ContainsKey(key))
			MultiValueDictionary[key].Add(value);
		else if (SingleValueDictionary.TryGetValue(key, out var previousValue))
		{
			MultiValueDictionary[key] = new List<TValue>() { previousValue, value };
		}
		else
			SingleValueDictionary[key] = value;
	}

	public TValue PopValue(TKey key)
	{
		if (MultiValueDictionary.TryGetValue(key, out var valueList))
		{
			var value = valueList[0];
			valueList.RemoveAt(0);

			if (valueList.Count == 0)
				MultiValueDictionary.Remove(key);

			return value;
		}
		else if (SingleValueDictionary.TryGetValue(key, out var previousValue))
		{
			SingleValueDictionary.Remove(key);
			return previousValue;
		}
		else
			throw new KeyNotFoundException();
	}
}