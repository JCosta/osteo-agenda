# Osteo Agenda

Agenda e notificações da clínica (substitui o Google Apps Script). Arquitetura completa no projeto Osteo: `arquitetura/agenda-notificacoes.md`.

**Estado (marco 1):** esquema da base de dados, login do backoffice, importação do Google Calendar (histórico + futuro, com pré-visualização) e ficha do cliente (próximas marcações, histórico, resumo). Notificações, página pública, bloqueios no backoffice e espelho do Calendar vêm nos marcos seguintes.

## Estrutura

```
src/Osteo.Agenda.Core   domínio, base de dados (EF Core + Npgsql), importação, consultas
  Data/Sql/*.sql         esquema (aplicado automaticamente no arranque, por ordem)
src/Osteo.Agenda.Web    backoffice (Razor Pages): /, /Contacts, /Import, /Login
tests/Osteo.Agenda.Tests testes (parser, planeamento da importação, base de dados real)
```

Regras garantidas pela própria base de dados:
- duas marcações ativas da mesma profissional nunca se sobrepõem (`EXCLUDE USING gist`);
- nenhuma marcação ativa cai dentro de um bloqueio de agenda (trigger).

## Correr localmente

Requer .NET 10 SDK e PostgreSQL 16.

```bash
# base de dados
createdb osteo_agenda

# palavra-passe do backoffice -> copiar o resultado
dotnet run --project src/Osteo.Agenda.Web -- hash-password "a-minha-palavra-passe"

export Admin__Email="joana@exemplo.pt"
export Admin__PasswordHash="pbkdf2-sha256$...."
dotnet run --project src/Osteo.Agenda.Web
```

A ligação usa `ConnectionStrings:Agenda` (ver `appsettings.Development.json`) ou `DATABASE_URL`.

### Testes

```bash
dotnet test                                   # testes sem base de dados
OSTEO_TEST_DB="Host=localhost;Username=postgres;Password=postgres" dotnet test   # inclui os de base de dados
```

## Ligar ao Google Calendar (só leitura, para importar)

1. Google Cloud Console → criar projeto → ativar **Google Calendar API**.
2. IAM → **Contas de serviço** → criar → separador *Chaves* → *Adicionar chave* → JSON.
3. No Google Calendar, em cada calendário da clínica: *Definições e partilha* → *Partilhar com pessoas específicas* → email da conta de serviço → **Ver todos os detalhes do evento**.
4. O **ID do calendário** está em *Integrar calendário* (ex. `abc123@group.calendar.google.com`, ou o email da conta para o calendário principal).
5. Definir `GoogleCalendar__ServiceAccountJson` com o conteúdo completo do JSON e `Clinic__Locations__0__GoogleCalendarId` com o ID.

## Importação

`/Import` → escolher local, intervalo (por omissão: últimos 24 meses + próximos 12) → **Ver** mostra o que vai acontecer a cada evento → **Importar**.

| No calendário | Na app |
|---|---|
| `Nome #Telefone` | contacto (agrupado por telefone) + paciente + marcação |
| `✅` no início ou cor verde | Confirmada |
| cor laranja | Pede remarcação |
| evento já passado | Realizada (histórico) |
| `#sms` / `#email` | canal preferido do contacto |
| outra `#tag` | guardada como serviço nas notas |
| `#bloqueio` | bloqueio de agenda |
| sem telefone, dia inteiro, sobreposição futura | ignorado (aparece a vermelho na pré-visualização) |
| mesmo telefone, nomes diferentes | um contacto com vários pacientes (irmãos) |

Nenhuma mensagem é enviada. Repetir a importação atualiza (pelo id do evento Google) em vez de duplicar. Decisões tomadas na app (cancelada, faltou) não são reescritas pelo calendário.

## Deploy no Render

1. Pôr este código num repositório GitHub.
2. Render → **New → Blueprint** → escolher o repositório (usa `render.yaml`: web Starter + Postgres Basic 256 MB, Frankfurt).
3. Preencher as variáveis marcadas como secretas: `Admin__Email`, `Admin__PasswordHash`, `GoogleCalendar__ServiceAccountJson`, `Clinic__Locations__0__GoogleCalendarId`.
4. O esquema é criado no primeiro arranque. Saúde em `/healthz`.

Para um segundo local: `Clinic__Locations__1__Name`, `Clinic__Locations__1__GoogleCalendarId`.
