# Serviços de terceiros: qualificação e propostas comerciais

Exigência: Normas PIPE 8.1 c, c.1, c.2 e c.7 e 9.2.1 e. Para **cada** serviço, anexar: (1) proposta comercial do prestador com detalhamento do serviço, valor e entregáveis; (2) qualificação do prestador (localização, nº de colaboradores, data de constituição, tempo de mercado, principais clientes, experiência no serviço); (3) justificativa da escolha frente a outros fornecedores. A propriedade intelectual resultante deve ser da empresa (c.6).

Os valores abaixo são os da Tabela 11 do projeto. Os campos `[ ]` só podem ser preenchidos com a proposta real do fornecedor.

## Resumo

| Serviço | Natureza no SAGe | Valor (R$) | Atividade | Prestador |
|---|---|---:|---|---|
| S1. Hospedagem e PostgreSQL/PostGIS gerenciado | Serviço de terceiros – PJ | 6.300,00 | A1–A7 | [provedor de nuvem] |
| S2. Testes de carga em nuvem | Serviço de terceiros – PJ | 2.000,00 | A5 | [ex.: Microsoft (Azure Load Testing)] |
| S3. Avaliação heurística e de acessibilidade | Serviço de terceiros – PJ | 5.000,00 | A6 | [empresa de UX] |
| S4. Consultoria jurídica | Serviço de terceiros – consultoria | 10.000,00 | A7 | [advogado, incluído na equipe] |
| **Total** | | **23.300,00** | | |

Limite de consultoria (c.2): 10% de R$ 300.000 = R$ 30.000. Usado: R$ 10.000.

## S1 e S2 — Provedores de nuvem

Provedores de nuvem não emitem proposta comercial personalizada para valores pequenos. Usar como proposta a **estimativa da calculadora oficial de preços**, salva em PDF com data, e anexar:

- S1: 2 ambientes × 9 meses: aplicação (plano básico com 1 vCPU e 2 GB) + PostgreSQL gerenciado com extensão PostGIS habilitável, região Brasil. Meta: até R$ 350 por ambiente-mês. Comparar pelo menos dois provedores (ex.: Azure Database for PostgreSQL – Flexible Server, Amazon RDS for PostgreSQL, Google Cloud SQL) e justificar pelo preço e pelo suporte a PostGIS.
- S2: 2.000 horas de usuário virtual (4 rodadas × 500 usuários × 1 h). Preço de referência: US$ 0,15/VUH (Azure Load Testing). Alternativa a comparar: k6 Cloud (Grafana Labs).
- Qualificação: são empresas de grande porte com atuação global; anexar a página institucional e a da região Brasil.
- Entregáveis: ambientes ativos durante o projeto; relatórios de execução dos testes de carga (A5).

## S3 — Avaliação heurística e de acessibilidade (empresa de UX)

**Modelo de proposta comercial a pedir ao fornecedor**

- Contratante: Dirija Tecnologia Ltda. (a constituir).
- Objeto: avaliação heurística (heurísticas de Nielsen) e de acessibilidade (WCAG 2.2, nível AA) da interface web mobile da plataforma Dirija!, por 3 avaliadores sem participação no desenvolvimento.
- Escopo: fluxos de busca em mapa, perfil do instrutor, reserva, pagamento simulado, Minhas aulas, avaliação e painel do instrutor; em 3 aparelhos (Android de entrada, Android intermediário, iPhone).
- Esforço: 3 avaliadores × 8 h, mais consolidação.
- Entregáveis: relatório com problemas encontrados, heurística violada, gravidade (escala de 0 a 4) e recomendação; reunião de apresentação.
- Prazo: mês 7, antes da segunda rodada de testes com alunos.
- Valor: R$ [≤ 5.000,00]. Forma de pagamento: [ ].
- Cláusula de PI: relatórios e resultados pertencem à contratante.
- Qualificação do prestador: [razão social, CNPJ, cidade, nº de colaboradores, data de constituição, clientes, portfólio de avaliações de usabilidade].
- Justificativa da escolha: [comparar com ao menos mais uma cotação].

## S4 — Consultoria jurídica

**Modelo de proposta comercial a pedir ao consultor**

- Consultor: [nome, OAB/SP nº] (incluído na equipe como Consultor; Súmula em `sumula-consultor-juridico.md`).
- Objeto: (1) termos de uso da plataforma; (2) política de privacidade conforme a LGPD, incluindo tratamento de dados de geolocalização; (3) modelo de contrato entre a plataforma e o instrutor autônomo (intermediação e taxa de 15%); (4) parecer sobre a compatibilidade da operação com a Resolução CONTRAN nº 1.020/2025 e sobre o risco da ADI 7978; (5) termo de consentimento para os testes com usuários (A6).
- Esforço: 40 h × R$ 250/h.
- Entregáveis: os cinco documentos acima, revisados com a equipe.
- Prazo: meses 7 a 9 (o termo de consentimento no início do mês 6, antes de A6).
- Valor: R$ 10.000,00. Forma de pagamento: [ ].
- Cláusula de PI: os documentos produzidos pertencem à contratante.
- Justificativa da escolha: [experiência em LGPD e plataformas; comparar com ao menos mais uma cotação].

> Observação: o termo de consentimento precisa estar pronto antes de A6 (mês 6). Se o consultor só começar no mês 7, a equipe usa um modelo de termo de consentimento da universidade e o consultor o revisa depois. Ajustar o cronograma se preferirem antecipar a consultoria.
