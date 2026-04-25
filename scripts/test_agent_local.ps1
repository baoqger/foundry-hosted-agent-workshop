Invoke-RestMethod -Method Post `
    -Uri "http://localhost:8088/responses" `
    -ContentType "application/json" `
    -Body '{"input":"We need an internal agent with private API access and workflow orchestration. Should we start with a hosted agent?"}'