-- Local SQLite (better-sqlite3). Single user, single machine: no owner column, no RLS.
-- Times are ISO-8601 UTC text. The operation catalog lives in code (src/ops/registry.ts), not here.
pragma foreign_keys = on;

create table settings (
  key   text primary key,                       -- modifier_convert, modifier_tools, modifier_actions, hotkey_menu
  value text not null
);

create table batches (
  id         integer primary key,
  created_at text not null default (strftime('%Y-%m-%dT%H:%M:%fZ','now'))
);

create table jobs (
  id          integer primary key,
  batch_id    integer not null references batches(id) on delete cascade,
  kind        text not null check (kind in ('convert','tool','action')),
  op_id       text not null,                    -- e.g. 'img.heic>jpg', 'pdf.compress'
  source_path text not null,
  params      text not null default '{}',       -- json
  status      text not null default 'queued'
              check (status in ('queued','running','done','failed','cancelled')),
  output_path text,
  error       text,
  counted     integer not null default 0,       -- 1 if it used a trial file (actions never do)
  created_at  text not null default (strftime('%Y-%m-%dT%H:%M:%fZ','now')),
  finished_at text
);
create index jobs_batch  on jobs(batch_id);
create index jobs_status on jobs(status);

create table license (
  id         integer primary key check (id = 1),
  files_used integer not null default 0,
  unlocked   integer not null default 0,
  key        text
);
insert into license(id) values (1);
