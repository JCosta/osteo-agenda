-- Esquema inicial: agenda, contactos, bloqueios, importação.
CREATE EXTENSION IF NOT EXISTS btree_gist;

CREATE TABLE locations (
    id                 uuid PRIMARY KEY,
    name               text NOT NULL UNIQUE,
    address            text,
    google_calendar_id text,
    morning_end        time NOT NULL DEFAULT '13:00',
    active             boolean NOT NULL DEFAULT true
);

CREATE TABLE staff (
    id               uuid PRIMARY KEY,
    name             text NOT NULL UNIQUE,
    email            text,
    telegram_chat_id text,
    active           boolean NOT NULL DEFAULT true
);

CREATE TABLE services (
    id                    uuid PRIMARY KEY,
    name                  text NOT NULL UNIQUE,
    duration_minutes      integer NOT NULL CHECK (duration_minutes > 0),
    buffer_before_minutes integer NOT NULL DEFAULT 0 CHECK (buffer_before_minutes >= 0),
    buffer_after_minutes  integer NOT NULL DEFAULT 0 CHECK (buffer_after_minutes >= 0),
    price                 numeric(10, 2),
    bookable_online       boolean NOT NULL DEFAULT false,
    active                boolean NOT NULL DEFAULT true
);

CREATE TABLE working_hours (
    id          uuid PRIMARY KEY,
    staff_id    uuid NOT NULL REFERENCES staff (id),
    location_id uuid NOT NULL REFERENCES locations (id),
    day_of_week integer NOT NULL CHECK (day_of_week BETWEEN 0 AND 6),
    start_time  time NOT NULL,
    end_time    time NOT NULL,
    CHECK (end_time > start_time)
);

CREATE TABLE contacts (
    id                         uuid PRIMARY KEY,
    name                       text NOT NULL,
    phone_e164                 text,
    phone_type                 text NOT NULL DEFAULT 'Unknown' CHECK (phone_type IN ('Unknown', 'Mobile', 'Landline')),
    email                      text,
    preferred_channel          text NOT NULL DEFAULT 'WhatsApp' CHECK (preferred_channel IN ('WhatsApp', 'Sms', 'Email')),
    whatsapp_unavailable_since timestamptz,
    consent_reminders          boolean NOT NULL DEFAULT true,
    consent_marketing          boolean NOT NULL DEFAULT false,
    notes                      text,
    created_at                 timestamptz NOT NULL DEFAULT now()
);
CREATE UNIQUE INDEX ux_contacts_phone ON contacts (phone_e164) WHERE phone_e164 IS NOT NULL;
CREATE INDEX ix_contacts_name ON contacts (lower(name));

CREATE TABLE patients (
    id           uuid PRIMARY KEY,
    contact_id   uuid NOT NULL REFERENCES contacts (id) ON DELETE CASCADE,
    name         text NOT NULL,
    birth_date   date,
    relationship text NOT NULL DEFAULT 'Self' CHECK (relationship IN ('Self', 'Child', 'Other'))
);
CREATE INDEX ix_patients_contact ON patients (contact_id);

CREATE TABLE appointments (
    id                   uuid PRIMARY KEY,
    staff_id             uuid NOT NULL REFERENCES staff (id),
    location_id          uuid NOT NULL REFERENCES locations (id),
    service_id           uuid REFERENCES services (id),
    patient_id           uuid NOT NULL REFERENCES patients (id),
    contact_id           uuid NOT NULL REFERENCES contacts (id),
    starts_at            timestamptz NOT NULL,
    ends_at              timestamptz NOT NULL,
    status               text NOT NULL CHECK (status IN ('Booked', 'AwaitingConfirmation', 'Confirmed', 'RescheduleRequested', 'Completed', 'NoShow', 'Cancelled')),
    source               text NOT NULL CHECK (source IN ('App', 'Online', 'Calendar', 'Import')),
    external_calendar_id text,
    external_event_id    text,
    internal_notes       text,
    public_token         text NOT NULL UNIQUE,
    version              integer NOT NULL DEFAULT 1,
    created_at           timestamptz NOT NULL DEFAULT now(),
    updated_at           timestamptz NOT NULL DEFAULT now(),
    CHECK (ends_at > starts_at),
    -- Duas marcações ativas da mesma profissional nunca se sobrepõem.
    CONSTRAINT ex_appointments_no_overlap EXCLUDE USING gist (
        staff_id WITH =,
        tstzrange(starts_at, ends_at) WITH &&
    ) WHERE (status IN ('Booked', 'AwaitingConfirmation', 'Confirmed', 'RescheduleRequested'))
);
CREATE UNIQUE INDEX ux_appointments_external ON appointments (external_calendar_id, external_event_id) WHERE external_event_id IS NOT NULL;
CREATE INDEX ix_appointments_contact_start ON appointments (contact_id, starts_at DESC);
CREATE INDEX ix_appointments_patient_start ON appointments (patient_id, starts_at DESC);
CREATE INDEX ix_appointments_staff_start ON appointments (staff_id, starts_at);

CREATE TABLE time_off (
    id                   uuid PRIMARY KEY,
    staff_id             uuid NOT NULL REFERENCES staff (id),
    location_id          uuid REFERENCES locations (id),
    starts_at            timestamptz NOT NULL,
    ends_at              timestamptz NOT NULL,
    kind                 text NOT NULL CHECK (kind IN ('FullDay', 'Morning', 'Afternoon', 'Custom')),
    reason               text,
    source               text NOT NULL CHECK (source IN ('App', 'Calendar')),
    external_calendar_id text,
    external_event_id    text,
    created_at           timestamptz NOT NULL DEFAULT now(),
    CHECK (ends_at > starts_at)
);
CREATE UNIQUE INDEX ux_time_off_external ON time_off (external_calendar_id, external_event_id) WHERE external_event_id IS NOT NULL;
CREATE INDEX ix_time_off_staff_start ON time_off (staff_id, starts_at);

-- Uma marcação ativa não pode cair dentro de um bloqueio de agenda.
CREATE FUNCTION appointments_not_in_time_off() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
    IF NEW.status IN ('Booked', 'AwaitingConfirmation', 'Confirmed', 'RescheduleRequested') AND EXISTS (
        SELECT 1 FROM time_off t
        WHERE t.staff_id = NEW.staff_id
          AND (t.location_id IS NULL OR t.location_id = NEW.location_id)
          AND tstzrange(t.starts_at, t.ends_at) && tstzrange(NEW.starts_at, NEW.ends_at)
    ) THEN
        RAISE EXCEPTION 'Marcação sobreposta a um bloqueio de agenda'
            USING ERRCODE = 'exclusion_violation', CONSTRAINT = 'appointments_not_in_time_off';
    END IF;
    RETURN NEW;
END;
$$;

CREATE TRIGGER trg_appointments_not_in_time_off
    BEFORE INSERT OR UPDATE OF starts_at, ends_at, status, staff_id, location_id ON appointments
    FOR EACH ROW EXECUTE FUNCTION appointments_not_in_time_off();

CREATE TABLE appointment_events (
    id             bigint GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
    appointment_id uuid NOT NULL REFERENCES appointments (id) ON DELETE CASCADE,
    type           text NOT NULL,
    data           jsonb,
    actor          text NOT NULL,
    occurred_at    timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX ix_appointment_events_appointment ON appointment_events (appointment_id, occurred_at);

-- Chaves de proteção de dados do ASP.NET Core (cookies de sessão sobrevivem a deploys).
CREATE TABLE data_protection_keys (
    id            integer GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
    friendly_name text,
    xml           text
);
