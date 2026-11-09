# sample-agente-24x7

Agente de IA **24/7** que vigia uma aplicação .NET em produção, investiga problemas com ferramentas e abre incidentes, usando **.NET 10**, **Microsoft.Extensions.AI** e o **gpt-5.4-mini** no **Microsoft Foundry**.

- `BackgroundService` com `PeriodicTimer`: um ciclo por vez, com tempo máximo por ciclo
- Regra primeiro, IA depois: com a aplicação saudável, nenhum token é gasto
- Ferramentas (function calling): health check, erros recentes, abrir incidente, solicitar reinício
- Ação destrutiva só com aprovação humana
- Deduplicação de incidentes e orçamento diário de tokens
- Resiliência a cota do Foundry (HTTP 429): respeita o `retry-after`, desvia para um deployment reserva e, sem reserva, segue sem IA
- Health check do próprio agente (batimento) para a plataforma reiniciar se ele travar
- Modo simulado: sem Foundry configurado, roda inteiro localmente, sem custo

Artigo completo: [Criando um agente de IA 24/7 para aplicações .NET](https://henriquemauri.net/criando-um-agente-de-ia-24-7-para-aplicacoes-net/)

## Estrutura

```
src/
  Loja.Api/          Aplicação de exemplo vigiada (health check, erros, simulação de falha)
  Vigia.Agente/      O agente
    Agente/          Worker, ferramentas, sinais, incidentes, orçamento, batimento
    Infra/           IChatClient do Foundry, cliente resiliente a cota, cliente simulado
infra/
  deploy-containerapps.sh   Publicação no Azure Container Apps (1 réplica fixa)
Dockerfile
```

## Rodando localmente (modo simulado)

```bash
# terminal 1: a aplicação vigiada
dotnet run --project src/Loja.Api

# terminal 2: o agente (ciclo de 10 segundos para a demo)
dotnet run --project src/Vigia.Agente -- --Vigia:IntervaloDoCiclo=00:00:10

# terminal 3: derrube o banco e gere erros
curl -X POST http://localhost:5100/simular/falha
for i in $(seq 1 8); do curl -s http://localhost:5100/pedidos/$i > /dev/null; done

# veja o que o agente fez
curl http://localhost:5200/incidentes
curl http://localhost:5200/aprovacoes
curl http://localhost:5200/status
```

## Conectando no Microsoft Foundry

1. Faça o deploy do `gpt-5.4-mini` no Foundry (de preferência **Global Standard**).
2. Dê ao seu usuário (local) ou à identidade do container (produção) a role **Cognitive Services OpenAI User**.
3. Configure:

```bash
cd src/Vigia.Agente
dotnet user-secrets init
dotnet user-secrets set "Foundry:Endpoint" "https://SEU-RECURSO.openai.azure.com/"

# opcional: deployment reserva em outra região, usado quando a cota do principal estoura
dotnet user-secrets set "Foundry:EndpointReserva" "https://SEU-RECURSO-RESERVA.openai.azure.com/"
```

## Publicando 24/7

Veja `infra/deploy-containerapps.sh`. Pontos principais:

- `--min-replicas 1 --max-replicas 1`: nunca escala para zero e nunca roda duplicado
- Managed Identity com a role **Cognitive Services OpenAI User**: sem chave
- Liveness probe em `/health`: se o agente parar de bater, o container é reiniciado

## Licença

MIT

---

Feito por [Henrique Mauri](https://henriquemauri.net) | [Dotcode](https://dotcode.com.br)
