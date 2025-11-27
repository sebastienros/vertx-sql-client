# Notes

- `PgSocketConnection.DecodeRow` is boxing the decoded values as it returns an `object[]`. We could create dedicated tuples based on the `PgColumnDesc[]` information and cache this type. Then dynamically instantiate it with the decoded value typed to the decriptor.
