-- Creates the local development database and a dedicated role for it.
--
-- Run once, as the postgres superuser:
--
--   & "C:\Program Files\PostgreSQL\18\bin\psql.exe" -U postgres -h localhost -f tools\create-dev-database.sql
--
-- The app gets its own account rather than the superuser. That keeps the
-- superuser password out of a file committed to git, and means a mistake in
-- the app cannot drop other databases on the same server.

CREATE DATABASE honeybee;

CREATE USER honeybee WITH PASSWORD 'honeybee_dev';

GRANT ALL PRIVILEGES ON DATABASE honeybee TO honeybee;

-- From PostgreSQL 15 on, CREATE on the public schema is no longer granted to
-- everyone by default, so EF migrations cannot create tables without this.
\connect honeybee

GRANT ALL ON SCHEMA public TO honeybee;
ALTER SCHEMA public OWNER TO honeybee;
