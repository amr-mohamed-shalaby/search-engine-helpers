# Golden-test fixture layout

The real CSV/XML samples were not included with the initial V2 specification. Place the owner-supplied files here (or in another directory and set `CSV_XML_GOLDEN_DIR` to that directory):

```text
golden-root/
  names.json
  golden-stamps.json
  <slug>_fields.csv
  <slug>_data.csv
  expected/
    <slug>/
      Project.xml
      Project_Fields.xml
      Project_Data.xml
      Project_lookup_Fields.xml
```

`golden-stamps.json` is a JSON object that maps each tested slug to its original V1 timestamp, for example:

```json
{
  "aladara_alaama_llahsae_w_altqaryr_hyeh_altnmya_alsnaaya": "11/11/2026 09:52:32 AM"
}
```

The integration test compares all four generated XML files byte-for-byte. Registry departments with no CSV files and no expected directory are ignored. If any fixture file for a department is present, the test requires both CSV files, the expected directory, and all four expected XML files. At least one complete department is required once the environment variable is set.
