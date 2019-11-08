-- Copyright (C) 2017 Alaa Masoud
-- See the LICENSE file in the project root for more information.

CREATE TABLE IF NOT EXISTS log(
	id INTEGER NOT NULL PRIMARY KEY,
	timestamp DATETIME NOT NULL,
	sourceApp TEXT NOT NULL collate nocase,
	message TEXT NOT NULL collate nocase,
	level INTEGER NOT NULL,
	exception TEXT NULL collate nocase
);

CREATE INDEX IF NOT EXISTS log_timestamp_idx ON log(timestamp);

CREATE INDEX IF NOT EXISTS log_sourceApp_idx ON log(sourceApp collate nocase);

CREATE TABLE IF NOT EXISTS log_property(
	logId INTEGER NOT NULL,
	timestamp DATETIME NOT NULL,
	name TEXT NOT NULL collate nocase,
	value TEXT NULL collate nocase
);

--CREATE INDEX IF NOT EXISTS log_property_logId_idx ON log_property(logId collate nocase);

CREATE INDEX IF NOT EXISTS log_property_name_idx ON log_property(name collate nocase);

--CREATE INDEX IF NOT EXISTS log_property_value_idx ON log_property(value collate nocase);

CREATE INDEX IF NOT EXISTS log_property_timestamp_idx ON log_property(timestamp);

CREATE TABLE IF NOT EXISTS log_query(
	id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
	name VARCHAR(255) NOT NULL,
	query TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS log_query_name_idx ON log_query(name);
