#!/usr/bin/env bash
# Arranca a app publicada contra o Postgres do CI, faz login, cria dados de exemplo
# e guarda o HTML de cada página em ./smoke/ para inspeção.
set -euo pipefail

OUT=smoke
mkdir -p "$OUT"
export PGPASSWORD=postgres
psql -h localhost -U postgres -qc "DROP DATABASE IF EXISTS osteo_smoke" -c "CREATE DATABASE osteo_smoke"

HASH=$(dotnet publish-out/Osteo.Agenda.Web.dll hash-password "smoke-pass")
export ConnectionStrings__Agenda="Host=localhost;Database=osteo_smoke;Username=postgres;Password=postgres"
export Admin__Email="smoke@example.pt"
export Admin__PasswordHash="$HASH"
export ASPNETCORE_URLS="http://127.0.0.1:5080"
export ASPNETCORE_ENVIRONMENT=Production

dotnet publish-out/Osteo.Agenda.Web.dll > "$OUT/app.log" 2>&1 &
APP=$!
trap 'kill $APP 2>/dev/null || true' EXIT

for _ in $(seq 1 60); do
  curl -fs http://127.0.0.1:5080/healthz >/dev/null && break
  sleep 1
done
echo "healthz: $(curl -s -o /dev/null -w '%{http_code}' http://127.0.0.1:5080/healthz)" | tee "$OUT/status.txt"

# Dados de exemplo (o esquema e a profissional/local já foram criados no arranque).
psql -h localhost -U postgres -d osteo_smoke -v ON_ERROR_STOP=1 -q <<'SQL'
INSERT INTO contacts (id, name, phone_e164, phone_type, preferred_channel)
VALUES ('11111111-1111-1111-1111-111111111111', 'Maria Silva', '+351912345678', 'Mobile', 'WhatsApp'),
       ('22222222-2222-2222-2222-222222222222', 'Ana Costa',   '+351933000111', 'Mobile', 'Sms');
INSERT INTO patients (id, contact_id, name, relationship)
VALUES ('aaaaaaaa-0000-0000-0000-000000000001', '11111111-1111-1111-1111-111111111111', 'Maria Silva', 'Self'),
       ('aaaaaaaa-0000-0000-0000-000000000002', '22222222-2222-2222-2222-222222222222', 'Tomás', 'Child'),
       ('aaaaaaaa-0000-0000-0000-000000000003', '22222222-2222-2222-2222-222222222222', 'Inês', 'Child');
INSERT INTO appointments (id, staff_id, location_id, patient_id, contact_id, starts_at, ends_at, status, source, public_token)
SELECT gen_random_uuid(), s.id, l.id, v.patient, v.contact, v.starts, v.starts + interval '1 hour', v.status, 'Import', md5(random()::text)
FROM staff s, locations l, (VALUES
  ('aaaaaaaa-0000-0000-0000-000000000001'::uuid, '11111111-1111-1111-1111-111111111111'::uuid, now() - interval '60 days', 'Completed'),
  ('aaaaaaaa-0000-0000-0000-000000000001'::uuid, '11111111-1111-1111-1111-111111111111'::uuid, now() - interval '20 days', 'NoShow'),
  ('aaaaaaaa-0000-0000-0000-000000000001'::uuid, '11111111-1111-1111-1111-111111111111'::uuid, date_trunc('day', now()) + interval '15 hours', 'Confirmed'),
  ('aaaaaaaa-0000-0000-0000-000000000001'::uuid, '11111111-1111-1111-1111-111111111111'::uuid, now() + interval '9 days', 'Booked'),
  ('aaaaaaaa-0000-0000-0000-000000000002'::uuid, '22222222-2222-2222-2222-222222222222'::uuid, now() - interval '10 days', 'Completed'),
  ('aaaaaaaa-0000-0000-0000-000000000003'::uuid, '22222222-2222-2222-2222-222222222222'::uuid, now() + interval '3 days', 'RescheduleRequested')
) AS v(patient, contact, starts, status);
SQL

JAR="$OUT/cookies.txt"
get() { curl -s -b "$JAR" -c "$JAR" -o "$OUT/$2.html" -w "$2: %{http_code}\n" "http://127.0.0.1:5080$1" | tee -a "$OUT/status.txt"; }

# Sem sessão: deve redirecionar para /Login
echo "anon /: $(curl -s -o /dev/null -w '%{http_code} -> %{redirect_url}' http://127.0.0.1:5080/)" | tee -a "$OUT/status.txt"

get /Login login
TOKEN=$(grep -o 'name="__RequestVerificationToken" type="hidden" value="[^"]*"' "$OUT/login.html" | sed 's/.*value="//;s/"$//')
echo "login post: $(curl -s -b "$JAR" -c "$JAR" -o /dev/null -w '%{http_code} -> %{redirect_url}' \
  --data-urlencode "Email=smoke@example.pt" --data-urlencode "Password=smoke-pass" \
  --data-urlencode "__RequestVerificationToken=$TOKEN" http://127.0.0.1:5080/Login)" | tee -a "$OUT/status.txt"

get / index
get "/Contacts" contacts
get "/Contacts?q=ines" contacts-search-ines
get "/Contacts?q=933" contacts-search-phone
get "/Contacts/Details/11111111-1111-1111-1111-111111111111" details-maria
get "/Contacts/Details/22222222-2222-2222-2222-222222222222" details-ana
get /Import import

# Texto visível de cada página (sem tags) para leitura rápida.
for f in "$OUT"/*.html; do
  sed -e 's/<[^>]*>/ /g' -e 's/&#xE7;/ç/g;s/&#xE3;/ã/g;s/&#xE9;/é/g;s/&#xEA;/ê/g;s/&#xF3;/ó/g;s/&#xED;/í/g;s/&#xE1;/á/g;s/&#xFA;/ú/g;s/&#xE0;/à/g;s/&#xF5;/õ/g;s/&#xC9;/É/g;s/&#xB7;/·/g;s/&#x2014;/—/g;s/&#x2013;/–/g' "$f" \
    | tr -s ' \t' ' ' | grep -v '^\s*$' > "${f%.html}.txt"
done
