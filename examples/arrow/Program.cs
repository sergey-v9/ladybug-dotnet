using Apache.Arrow;
using Apache.Arrow.Types;
using LadybugDB;
using LadybugDB.Arrow;

// Apache Arrow interop (LadybugDB.Arrow): export a query result to Arrow RecordBatches, and ingest a
// RecordBatch as a node table.

using Database database = new();
using Connection connection = new(database);

connection.Query("CREATE NODE TABLE Person(name STRING, age INT64, PRIMARY KEY(name))").Dispose();
for (int i = 0; i < 3; i++)
{
    connection.Query($"CREATE (:Person {{name: 'P{i}', age: {30 + i}}})").Dispose();
}

// --- Export: QueryResult -> Arrow ---
using (QueryResult result = connection.Query("MATCH (p:Person) RETURN p.name AS name, p.age AS age"))
{
    Schema schema = result.ReadSchema();
    Console.WriteLine("Arrow schema:");
    foreach (Field field in schema.FieldsList)
    {
        Console.WriteLine($"  {field.Name}: {field.DataType.TypeId}");
    }

    long rows = 0;
    foreach (RecordBatch batch in result.ReadBatches())
    {
        rows += batch.Length;
        batch.Dispose();
    }

    Console.WriteLine($"exported {rows} rows as Arrow record batches");
}

// --- Ingest: Arrow RecordBatch -> table ---
StringArray names = new StringArray.Builder().Append("Carol").Append("Dave").Build();
Int64Array ages = new Int64Array.Builder().Append(40).Append(45).Build();
Schema ingestSchema = new Schema(
    new[]
    {
        new Field("name", StringType.Default, nullable: false),
        new Field("age", Int64Type.Default, nullable: false),
    },
    metadata: null);

using RecordBatch incoming = new RecordBatch(ingestSchema, new IArrowArray[] { names, ages }, length: 2);
connection.CreateArrowTable("Imported", incoming);
Console.WriteLine("ingested 2 rows via CreateArrowTable('Imported', batch)");

using QueryResult imported = connection.Query("MATCH (n:Imported) RETURN count(*) AS n");
Console.WriteLine($"Imported table now has {imported.Rows().First()[0]} rows");
