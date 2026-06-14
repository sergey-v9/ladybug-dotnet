using LadybugDB;
using LadybugDB.Mapping;

// Source-generated POCO mapping: annotate a record with [LadybugRow], then materialize result rows
// into it with the generated, reflection-free Map<T>() extension (also MapAsync<T>()).

using Database database = new();
using Connection connection = new(database);

connection.Query("CREATE NODE TABLE Person(name STRING, age INT64, registered DATE, PRIMARY KEY(name))").Dispose();
connection.Query("CREATE (:Person {name: 'Alice', age: 30, registered: date('2021-05-01')})").Dispose();
connection.Query("CREATE (:Person {name: 'Bob', age: 25, registered: date('2023-11-20')})").Dispose();

// Column aliases are matched to the record members by name, case-insensitively.
using QueryResult result = connection.Query(
    "MATCH (p:Person) RETURN p.name AS Name, p.age AS Age, p.registered AS Registered ORDER BY p.name");

foreach (Person person in result.Map<Person>())
{
    Console.WriteLine($"{person.Name} is {person.Age}, registered {person.Registered:yyyy-MM-dd}");
}

[LadybugRow]
public sealed record Person(string Name, long Age, DateOnly Registered);
