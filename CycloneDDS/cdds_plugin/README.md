# Array schema and sample ownership

`Float32MultiArray_` contains `MultiArrayLayout_ layout` followed by `sequence<float> data`.
The previous data-only schema is incompatible with this schema. Rebuild peers using that old IDL.
All other message schemas and public message type IDs are unchanged.

`cdds_publish_float32_multi_array(writer, data, count)` sends an empty dimension list and zero
data offset. `count == 0` permits a null data pointer. The pointer is borrowed only during the call.
For nonempty layout metadata, use the generated struct with `cdds_publish_raw`.

For raw receives, allocate once using `cdds_sample_alloc(type)`, repeatedly call
`cdds_take_raw(reader, sample, cdds_sample_size(type))`, and free once with
`cdds_sample_free(type, sample)`. The reader and sample must have the same type.
The sample owns all nested strings/sequences, including after reader destruction or shutdown.
Nested pointers may change on the next take; do not retain them or modify ownership fields.
Do not pass uninitialized memory or return this sample as a reader loan.
Access to a sample must be serialized by its caller.

`cdds_take_raw` returns 1 for valid data, 0 for no valid data, and negative DDS errors.
It requires the exact native struct size. Existing scalar and geometry typed APIs are unchanged.

Configure with `BUILD_TESTING=ON`, build, then run `ctest --test-dir <build> -C Release --output-on-failure`.
The test uses domain 197 on loopback and checks sequence reuse, nested string ownership, full layout,
empty arrays, invalid arguments, and scalar regression. Android builds use `BUILD_TESTING=OFF`.
