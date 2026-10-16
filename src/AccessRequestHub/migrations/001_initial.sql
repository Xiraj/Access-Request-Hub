CREATE TABLE users (
    id TEXT PRIMARY KEY,
    name TEXT NOT NULL,
    email TEXT NOT NULL UNIQUE,
    role TEXT NOT NULL CHECK(role IN ('Requester','Manager','SystemOwner','Auditor')),
    manager_id TEXT REFERENCES users(id),
    CHECK(manager_id IS NULL OR manager_id <> id)
);

CREATE TABLE applications (
    id TEXT PRIMARY KEY,
    name TEXT NOT NULL UNIQUE,
    owner_id TEXT NOT NULL REFERENCES users(id)
);

CREATE TABLE access_requests (
    id TEXT PRIMARY KEY,
    client_request_id TEXT NOT NULL UNIQUE CHECK(length(client_request_id) BETWEEN 1 AND 100),
    requester_id TEXT NOT NULL REFERENCES users(id),
    manager_id TEXT NOT NULL REFERENCES users(id),
    owner_id TEXT NOT NULL REFERENCES users(id),
    application_id TEXT NOT NULL REFERENCES applications(id),
    environment TEXT NOT NULL CHECK(environment IN ('NonProduction','Production')),
    access_level TEXT NOT NULL CHECK(access_level IN ('Read','Admin')),
    justification TEXT NOT NULL CHECK(length(trim(justification)) BETWEEN 1 AND 2000),
    status TEXT NOT NULL CHECK(status IN ('AwaitingManager','AwaitingOwner','Approved','Rejected')),
    policy_version TEXT NOT NULL CHECK(policy_version = 'v1'),
    version INTEGER NOT NULL CHECK(typeof(version) = 'integer' AND version >= 1),
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL,
    last_actor_id TEXT NOT NULL REFERENCES users(id),
    last_action TEXT NOT NULL CHECK(last_action IN ('submitted','approved','rejected')),
    last_reason TEXT,
    CHECK(requester_id <> manager_id),
    CHECK((last_action = 'rejected' AND last_reason IS NOT NULL AND length(trim(last_reason)) BETWEEN 1 AND 2000)
       OR (last_action <> 'rejected' AND last_reason IS NULL))
);

CREATE TABLE audit_events (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    request_id TEXT NOT NULL REFERENCES access_requests(id),
    actor_id TEXT NOT NULL REFERENCES users(id),
    action TEXT NOT NULL CHECK(action IN ('submitted','approved','rejected')),
    from_status TEXT,
    to_status TEXT NOT NULL,
    reason TEXT,
    request_version INTEGER NOT NULL,
    policy_version TEXT NOT NULL,
    occurred_at TEXT NOT NULL,
    UNIQUE(request_id, request_version)
);

CREATE INDEX requests_requester ON access_requests(requester_id, created_at);
CREATE INDEX requests_manager ON access_requests(manager_id, status);
CREATE INDEX requests_owner ON access_requests(owner_id, status);
CREATE INDEX audits_request ON audit_events(request_id, id);

CREATE TRIGGER request_insert_guard BEFORE INSERT ON access_requests BEGIN
    SELECT CASE WHEN NEW.status <> 'AwaitingManager' OR NEW.version <> 1
      OR NEW.last_action <> 'submitted' OR NEW.last_actor_id <> NEW.requester_id
      THEN RAISE(ABORT, 'invalid initial state') END;
    SELECT CASE WHEN NEW.manager_id IS NOT (SELECT manager_id FROM users WHERE id = NEW.requester_id)
      OR NEW.owner_id IS NOT (SELECT owner_id FROM applications WHERE id = NEW.application_id)
      THEN RAISE(ABORT, 'invalid approver snapshot') END;
END;

CREATE TRIGGER request_update_guard BEFORE UPDATE ON access_requests BEGIN
    SELECT CASE WHEN NEW.id IS NOT OLD.id OR NEW.client_request_id IS NOT OLD.client_request_id
      OR NEW.requester_id IS NOT OLD.requester_id OR NEW.manager_id IS NOT OLD.manager_id
      OR NEW.owner_id IS NOT OLD.owner_id OR NEW.application_id IS NOT OLD.application_id
      OR NEW.environment IS NOT OLD.environment OR NEW.access_level IS NOT OLD.access_level
      OR NEW.justification IS NOT OLD.justification OR NEW.policy_version IS NOT OLD.policy_version
      OR NEW.created_at IS NOT OLD.created_at
      THEN RAISE(ABORT, 'request facts are immutable') END;
    SELECT CASE WHEN OLD.status IN ('Approved','Rejected') THEN RAISE(ABORT, 'terminal request') END;
    SELECT CASE WHEN NEW.version <> OLD.version + 1 THEN RAISE(ABORT, 'invalid version increment') END;
    SELECT CASE WHEN NEW.last_actor_id = OLD.requester_id
      OR (OLD.status = 'AwaitingManager' AND NEW.last_actor_id <> OLD.manager_id)
      OR (OLD.status = 'AwaitingOwner' AND NEW.last_actor_id <> OLD.owner_id)
      THEN RAISE(ABORT, 'invalid decision actor') END;
    SELECT CASE WHEN NOT (
      (NEW.last_action = 'rejected' AND NEW.status = 'Rejected') OR
      (NEW.last_action = 'approved' AND OLD.status = 'AwaitingOwner' AND NEW.status = 'Approved') OR
      (NEW.last_action = 'approved' AND OLD.status = 'AwaitingManager' AND NEW.status =
         CASE WHEN OLD.environment = 'Production' OR OLD.access_level = 'Admin'
              THEN 'AwaitingOwner' ELSE 'Approved' END)
      ) THEN RAISE(ABORT, 'illegal state transition') END;
END;

CREATE TRIGGER request_no_delete BEFORE DELETE ON access_requests BEGIN
    SELECT RAISE(ABORT, 'requests cannot be deleted');
END;

CREATE TRIGGER audit_on_create AFTER INSERT ON access_requests BEGIN
    INSERT INTO audit_events(
        request_id,actor_id,action,from_status,to_status,reason,
        request_version,policy_version,occurred_at)
    VALUES(
        NEW.id,NEW.last_actor_id,'submitted',NULL,NEW.status,NULL,
        NEW.version,NEW.policy_version,NEW.created_at);
END;

CREATE TRIGGER audit_on_transition AFTER UPDATE ON access_requests BEGIN
    INSERT INTO audit_events(
        request_id,actor_id,action,from_status,to_status,reason,
        request_version,policy_version,occurred_at)
    VALUES(
        NEW.id,NEW.last_actor_id,NEW.last_action,OLD.status,NEW.status,
        NEW.last_reason,NEW.version,NEW.policy_version,NEW.updated_at);
END;

CREATE TRIGGER audit_no_update BEFORE UPDATE ON audit_events BEGIN
    SELECT RAISE(ABORT, 'audit is append-only');
END;

CREATE TRIGGER audit_no_delete BEFORE DELETE ON audit_events BEGIN
    SELECT RAISE(ABORT, 'audit is append-only');
END;

INSERT INTO users VALUES ('bob','Bob','bob@example.local','Manager',NULL);
INSERT INTO users VALUES ('alice','Alice','alice@example.local','Requester','bob');
INSERT INTO users VALUES ('carol','Carol','carol@example.local','SystemOwner',NULL);
INSERT INTO users VALUES ('dana','Dana','dana@example.local','SystemOwner',NULL);
INSERT INTO users VALUES ('erin','Erin','erin@example.local','Auditor',NULL);

INSERT INTO applications VALUES ('crm','CRM','carol');
INSERT INTO applications VALUES ('finance','Finance Portal','dana');

