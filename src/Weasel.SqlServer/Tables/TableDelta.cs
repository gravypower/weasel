using System;
using System.IO;
using System.Linq;
using Baseline;
using Weasel.Core;

namespace Weasel.SqlServer.Tables
{
    public class TableDelta : SchemaObjectDelta<Table>
    {
        public TableDelta(Table expected, Table? actual) : base(expected, actual)
        {
        }

        internal ItemDelta<TableColumn> Columns { get; private set; } = null!;
        internal ItemDelta<IndexDefinition> Indexes { get; private set; } = null!;

        internal ItemDelta<ForeignKey> ForeignKeys { get; private set; } = null!;

        public SchemaPatchDifference PrimaryKeyDifference { get; private set; }

        protected override SchemaPatchDifference compare(Table expected, Table? actual)
        {
            if (actual == null)
            {
                return SchemaPatchDifference.Create;
            }

            Columns = new ItemDelta<TableColumn>(expected.Columns, actual.Columns);
            Indexes = new ItemDelta<IndexDefinition>(expected.Indexes, actual.Indexes,
                (e, a) => e.Matches(a, Expected));

            ForeignKeys = new ItemDelta<ForeignKey>(expected.ForeignKeys, actual.ForeignKeys);

            PrimaryKeyDifference = SchemaPatchDifference.None;
            if (expected.PrimaryKeyName.IsEmpty())
            {
                if (actual.PrimaryKeyName.IsNotEmpty())
                {
                    PrimaryKeyDifference = SchemaPatchDifference.Update;
                }
            }
            else if (actual.PrimaryKeyName.IsEmpty())
            {
                PrimaryKeyDifference = SchemaPatchDifference.Create;
            }
            else if (!expected.PrimaryKeyColumns.SequenceEqual(actual.PrimaryKeyColumns))
            {
                PrimaryKeyDifference = SchemaPatchDifference.Update;
            }

            return determinePatchDifference();
        }

        public override void WriteUpdate(Migrator rules, TextWriter writer)
        {
            if (Difference == SchemaPatchDifference.Invalid)
            {
                throw new InvalidOperationException($"TableDelta for {Expected.Identifier} is invalid");
            }

            if (Difference == SchemaPatchDifference.Create)
            {
                SchemaObject.WriteCreateStatement(rules, writer);
                return;
            }

            // Extra indexes
            foreach (var extra in Indexes.Extras) writer.WriteDropIndex(Expected, extra);

            // Different indexes
            foreach (var change in Indexes.Different) writer.WriteDropIndex(Expected, change.Actual);

            // Missing columns
            foreach (var column in Columns.Missing) writer.WriteLine(column.AddColumnSql(Expected));


            // Different columns
            foreach (var change1 in Columns.Different)
                writer.WriteLine(change1.Expected.AlterColumnTypeSql(Expected, change1.Actual));

            writeForeignKeyUpdates(writer);

            // Missing indexes
            foreach (var indexDefinition in Indexes.Missing) writer.WriteLine(indexDefinition.ToDDL(Expected));

            // Different indexes
            foreach (var change in Indexes.Different) writer.WriteLine(change.Expected.ToDDL(Expected));


            // Extra columns
            foreach (var column in Columns.Extras) writer.WriteLine(column.DropColumnSql(Expected));


            switch (PrimaryKeyDifference)
            {
                case SchemaPatchDifference.Invalid:
                case SchemaPatchDifference.Update:
                    writer.WriteLine($"alter table {Expected.Identifier} drop constraint {Actual!.PrimaryKeyName};");
                    writer.WriteLine($"alter table {Expected.Identifier} add {Expected.PrimaryKeyDeclaration()};");
                    break;

                case SchemaPatchDifference.Create:
                    writer.WriteLine($"alter table {Expected.Identifier} add {Expected.PrimaryKeyDeclaration()};");
                    break;
            }
        }

        private void writeForeignKeyUpdates(TextWriter writer)
        {
            foreach (var foreignKey in ForeignKeys.Missing) foreignKey.WriteAddStatement(Expected, writer);

            foreach (var foreignKey in ForeignKeys.Extras) foreignKey.WriteDropStatement(Expected, writer);

            foreach (var change in ForeignKeys.Different)
            {
                change.Actual.WriteDropStatement(Expected, writer);
                change.Expected.WriteAddStatement(Expected, writer);
            }
        }

        public override void WriteRollback(Migrator rules, TextWriter writer)
        {
            if (Actual == null)
            {
                Expected.WriteDropStatement(rules, writer);
                return;
            }

            foreach (var foreignKey in ForeignKeys.Missing) foreignKey.WriteDropStatement(Expected, writer);

            foreach (var change in ForeignKeys.Different) change.Expected.WriteDropStatement(Expected, writer);

            // Extra columns
            foreach (var column in Columns.Extras) writer.WriteLine(column.AddColumnSql(Expected));

            // Different columns
            foreach (var change1 in Columns.Different)
                writer.WriteLine(change1.Actual.AlterColumnTypeSql(Actual, change1.Expected));

            foreach (var change in ForeignKeys.Different) change.Actual.WriteAddStatement(Expected, writer);

            rollbackIndexes(writer);

            // Missing columns
            foreach (var column in Columns.Missing) writer.WriteLine(column.DropColumnSql(Expected));

            foreach (var foreignKey in ForeignKeys.Extras) foreignKey.WriteAddStatement(Expected, writer);


            switch (PrimaryKeyDifference)
            {
                case SchemaPatchDifference.Invalid:
                case SchemaPatchDifference.Update:
                    writer.WriteLine(
                        $"alter table {Expected.Identifier} drop constraint if exists {Expected.PrimaryKeyName};");
                    writer.WriteLine($"alter table {Expected.Identifier} add {Actual.PrimaryKeyDeclaration()};");
                    break;

                case SchemaPatchDifference.Create:
                    writer.WriteLine(
                        $"alter table {Expected.Identifier} drop constraint if exists {Expected.PrimaryKeyName};");
                    break;
            }
        }

        private void rollbackIndexes(TextWriter writer)
        {
            // Missing indexes
            foreach (var indexDefinition in Indexes.Missing) writer.WriteDropIndex(Expected, indexDefinition);

            // Extra indexes
            foreach (var extra in Indexes.Extras) writer.WriteLine(extra.ToDDL(Actual!));

            // Different indexes
            foreach (var change in Indexes.Different)
            {
                writer.WriteDropIndex(Actual!, change.Expected);
                writer.WriteLine(change.Actual.ToDDL(Actual!));
            }
        }

        private SchemaPatchDifference determinePatchDifference()
        {
            if (Actual!.PartitionStrategy != Expected.PartitionStrategy)
            {
                return SchemaPatchDifference.Invalid;
            }

            if (!Actual.PartitionExpressions.SequenceEqual(Expected.PartitionExpressions))
            {
                return SchemaPatchDifference.Invalid;
            }


            if (!HasChanges())
            {
                return SchemaPatchDifference.None;
            }


            // If there are any columns that are different and at least one cannot
            // automatically generate an `ALTER TABLE` statement, the patch is invalid
            if (Columns.Different.Any(x => !x.Expected.CanAlter(x.Actual)))
            {
                return SchemaPatchDifference.Invalid;
            }

            // If there are any missing columns and at least one
            // cannot generate an `ALTER TABLE * ADD COLUMN` statement
            if (Columns.Missing.Any(x => !x.CanAdd()))
            {
                return SchemaPatchDifference.Invalid;
            }

            var differences = new[]
            {
                Columns.Difference(),
                ForeignKeys.Difference(),
                Indexes.Difference(),
                PrimaryKeyDifference
            };

            return differences.Min();
        }

        public bool HasChanges()
        {
            return Columns.HasChanges() || Indexes.HasChanges() || ForeignKeys.HasChanges() ||
                   PrimaryKeyDifference != SchemaPatchDifference.None;
        }

        public override string ToString()
        {
            return $"TableDelta for {Expected.Identifier}";
        }
    }
}
