#!/usr/bin/env pwsh
# Grafana Dashboard Provisioning Script
# Creates commonly used dashboards for the CoreSRE observability stack

$GRAFANA = "http://localhost:30300"
$PROM_UID = "afdisfa0o1pmod"
$LOKI_UID = "bfdisgep4inlsf"
$JAEGER_UID = "ffdishjdihp8gd"

function New-Dashboard($json) {
    $body = @{ dashboard = $json; overwrite = $true } | ConvertTo-Json -Depth 50 -Compress
    try {
        $r = Invoke-RestMethod -Method Post -Uri "$GRAFANA/api/dashboards/db" -ContentType "application/json" -Body $body
        Write-Host "  OK: $($r.url)" -ForegroundColor Green
    } catch {
        Write-Host "  FAIL: $_" -ForegroundColor Red
    }
}

# ============================================================
# Dashboard 1: Kubernetes Cluster Overview
# ============================================================
Write-Host "`n[1/4] Creating 'Kubernetes Cluster Overview' dashboard..." -ForegroundColor Cyan

$k8sDashboard = @{
    uid = "k8s-cluster-overview"
    title = "Kubernetes Cluster Overview"
    tags = @("kubernetes", "cluster")
    timezone = "browser"
    refresh = "30s"
    time = @{ from = "now-1h"; to = "now" }
    panels = @(
        # Row: Cluster Health
        @{ id = 100; type = "row"; title = "Cluster Health"; gridPos = @{ h = 1; w = 24; x = 0; y = 0 }; collapsed = $false }

        # Stat: Total Targets Up
        @{
            id = 1; type = "stat"; title = "Targets UP"
            gridPos = @{ h = 4; w = 4; x = 0; y = 1 }
            datasource = @{ type = "prometheus"; uid = $PROM_UID }
            targets = @(@{ refId = "A"; expr = "count(up == 1)"; legendFormat = "UP" })
            options = @{ colorMode = "background"; graphMode = "none"; textMode = "value" }
            fieldConfig = @{
                defaults = @{
                    thresholds = @{ mode = "absolute"; steps = @(
                        @{ color = "red"; value = $null }
                        @{ color = "orange"; value = 1 }
                        @{ color = "green"; value = 3 }
                    )}
                }
            }
        }

        # Stat: Total Targets Down
        @{
            id = 2; type = "stat"; title = "Targets DOWN"
            gridPos = @{ h = 4; w = 4; x = 4; y = 1 }
            datasource = @{ type = "prometheus"; uid = $PROM_UID }
            targets = @(@{ refId = "A"; expr = "count(up == 0) OR on() vector(0)"; legendFormat = "DOWN" })
            options = @{ colorMode = "background"; graphMode = "none"; textMode = "value" }
            fieldConfig = @{
                defaults = @{
                    thresholds = @{ mode = "absolute"; steps = @(
                        @{ color = "green"; value = $null }
                        @{ color = "red"; value = 1 }
                    )}
                }
            }
        }

        # Stat: Scrape Duration Avg
        @{
            id = 3; type = "stat"; title = "Avg Scrape Duration"
            gridPos = @{ h = 4; w = 4; x = 8; y = 1 }
            datasource = @{ type = "prometheus"; uid = $PROM_UID }
            targets = @(@{ refId = "A"; expr = "avg(scrape_duration_seconds)"; legendFormat = "" })
            options = @{ colorMode = "background"; graphMode = "area"; textMode = "value" }
            fieldConfig = @{ defaults = @{ unit = "s"; decimals = 4 } }
        }

        # Stat: Prometheus self uptime
        @{
            id = 4; type = "stat"; title = "Prometheus Uptime"
            gridPos = @{ h = 4; w = 4; x = 12; y = 1 }
            datasource = @{ type = "prometheus"; uid = $PROM_UID }
            targets = @(@{ refId = "A"; expr = "(time() - process_start_time_seconds{job=`"prometheus`"})"; legendFormat = "" })
            options = @{ colorMode = "background"; graphMode = "none"; textMode = "value" }
            fieldConfig = @{ defaults = @{ unit = "s" } }
        }

        # Stat: Alertmanager Alerts
        @{
            id = 5; type = "stat"; title = "Active Alerts"
            gridPos = @{ h = 4; w = 4; x = 16; y = 1 }
            datasource = @{ type = "prometheus"; uid = $PROM_UID }
            targets = @(@{ refId = "A"; expr = "alertmanager_alerts{state=`"active`"} OR on() vector(0)"; legendFormat = "" })
            options = @{ colorMode = "background"; graphMode = "none"; textMode = "value" }
            fieldConfig = @{
                defaults = @{
                    thresholds = @{ mode = "absolute"; steps = @(
                        @{ color = "green"; value = $null }
                        @{ color = "orange"; value = 1 }
                        @{ color = "red"; value = 5 }
                    )}
                }
            }
        }

        # Stat: Total Metrics
        @{
            id = 6; type = "stat"; title = "Total Series"
            gridPos = @{ h = 4; w = 4; x = 20; y = 1 }
            datasource = @{ type = "prometheus"; uid = $PROM_UID }
            targets = @(@{ refId = "A"; expr = "prometheus_tsdb_head_series"; legendFormat = "" })
            options = @{ colorMode = "background"; graphMode = "area"; textMode = "value" }
        }

        # Row: Target Health
        @{ id = 101; type = "row"; title = "Target Health Details"; gridPos = @{ h = 1; w = 24; x = 0; y = 5 }; collapsed = $false }

        # Table: All Targets Status
        @{
            id = 7; type = "table"; title = "All Scrape Targets"
            gridPos = @{ h = 8; w = 12; x = 0; y = 6 }
            datasource = @{ type = "prometheus"; uid = $PROM_UID }
            targets = @(@{
                refId = "A"
                expr = "up"
                format = "table"
                instant = $true
            })
            transformations = @(
                @{ id = "organize"; options = @{
                    excludeByName = @{ Time = $true; __name__ = $true }
                    renameByName = @{ Value = "Status (1=UP)"; job = "Job"; instance = "Instance" }
                }}
            )
        }

        # Time Series: Scrape Duration per Target
        @{
            id = 8; type = "timeseries"; title = "Scrape Duration by Target"
            gridPos = @{ h = 8; w = 12; x = 12; y = 6 }
            datasource = @{ type = "prometheus"; uid = $PROM_UID }
            targets = @(@{ refId = "A"; expr = "scrape_duration_seconds"; legendFormat = "{{job}} / {{instance}}" })
            fieldConfig = @{ defaults = @{ unit = "s" } }
        }

        # Row: Prometheus Internals
        @{ id = 102; type = "row"; title = "Prometheus Internals"; gridPos = @{ h = 1; w = 24; x = 0; y = 14 }; collapsed = $false }

        # Time Series: TSDB Head Series
        @{
            id = 9; type = "timeseries"; title = "TSDB Head Series"
            gridPos = @{ h = 7; w = 8; x = 0; y = 15 }
            datasource = @{ type = "prometheus"; uid = $PROM_UID }
            targets = @(@{ refId = "A"; expr = "prometheus_tsdb_head_series"; legendFormat = "series" })
        }

        # Time Series: Samples Scraped Rate
        @{
            id = 10; type = "timeseries"; title = "Samples Scraped / sec"
            gridPos = @{ h = 7; w = 8; x = 8; y = 15 }
            datasource = @{ type = "prometheus"; uid = $PROM_UID }
            targets = @(@{ refId = "A"; expr = "rate(prometheus_tsdb_head_samples_appended_total[5m])"; legendFormat = "appended/s" })
            fieldConfig = @{ defaults = @{ unit = "ops" } }
        }

        # Time Series: Rule Evaluation Duration
        @{
            id = 11; type = "timeseries"; title = "Alertmanager Notifications/s"
            gridPos = @{ h = 7; w = 8; x = 16; y = 15 }
            datasource = @{ type = "prometheus"; uid = $PROM_UID }
            targets = @(@{ refId = "A"; expr = "rate(alertmanager_notifications_total[5m])"; legendFormat = "{{integration}}" })
            fieldConfig = @{ defaults = @{ unit = "ops" } }
        }
    )
}

New-Dashboard $k8sDashboard

# ============================================================
# Dashboard 2: Demo App - Service Metrics (HTTP + Business)
# ============================================================
Write-Host "`n[2/4] Creating 'Demo App - Service Metrics' dashboard..." -ForegroundColor Cyan

$appDashboard = @{
    uid = "demo-app-metrics"
    title = "Demo App - Service Metrics"
    tags = @("demo-app", "http", "business")
    timezone = "browser"
    refresh = "15s"
    time = @{ from = "now-30m"; to = "now" }
    panels = @(
        # Row: HTTP Overview
        @{ id = 200; type = "row"; title = "HTTP Overview"; gridPos = @{ h = 1; w = 24; x = 0; y = 0 }; collapsed = $false }

        # Stat: Total Request Rate
        @{
            id = 20; type = "stat"; title = "Total Requests/sec"
            gridPos = @{ h = 4; w = 6; x = 0; y = 1 }
            datasource = @{ type = "prometheus"; uid = $PROM_UID }
            targets = @(@{ refId = "A"; expr = "sum(rate(http_requests_total[5m]))"; legendFormat = "" })
            options = @{ colorMode = "background"; graphMode = "area"; textMode = "value" }
            fieldConfig = @{ defaults = @{ unit = "reqps"; decimals = 2 } }
        }

        # Stat: Error Rate
        @{
            id = 21; type = "stat"; title = "Error Rate (5xx)"
            gridPos = @{ h = 4; w = 6; x = 6; y = 1 }
            datasource = @{ type = "prometheus"; uid = $PROM_UID }
            targets = @(@{ refId = "A"; expr = "sum(rate(http_requests_total{status=~`"5..`"}[5m])) / sum(rate(http_requests_total[5m])) * 100 OR on() vector(0)"; legendFormat = "" })
            options = @{ colorMode = "background"; graphMode = "none"; textMode = "value" }
            fieldConfig = @{
                defaults = @{
                    unit = "percent"; decimals = 2
                    thresholds = @{ mode = "absolute"; steps = @(
                        @{ color = "green"; value = $null }
                        @{ color = "orange"; value = 1 }
                        @{ color = "red"; value = 5 }
                    )}
                }
            }
        }

        # Stat: Avg Latency P50
        @{
            id = 22; type = "stat"; title = "P50 Latency"
            gridPos = @{ h = 4; w = 6; x = 12; y = 1 }
            datasource = @{ type = "prometheus"; uid = $PROM_UID }
            targets = @(@{ refId = "A"; expr = "histogram_quantile(0.50, sum(rate(http_request_duration_seconds_bucket[5m])) by (le))"; legendFormat = "" })
            options = @{ colorMode = "background"; graphMode = "area"; textMode = "value" }
            fieldConfig = @{ defaults = @{ unit = "s"; decimals = 3 } }
        }

        # Stat: P99 Latency
        @{
            id = 23; type = "stat"; title = "P99 Latency"
            gridPos = @{ h = 4; w = 6; x = 18; y = 1 }
            datasource = @{ type = "prometheus"; uid = $PROM_UID }
            targets = @(@{ refId = "A"; expr = "histogram_quantile(0.99, sum(rate(http_request_duration_seconds_bucket[5m])) by (le))"; legendFormat = "" })
            options = @{ colorMode = "background"; graphMode = "area"; textMode = "value" }
            fieldConfig = @{
                defaults = @{
                    unit = "s"; decimals = 3
                    thresholds = @{ mode = "absolute"; steps = @(
                        @{ color = "green"; value = $null }
                        @{ color = "orange"; value = 0.5 }
                        @{ color = "red"; value = 1 }
                    )}
                }
            }
        }

        # Time Series: Request Rate by Service
        @{
            id = 24; type = "timeseries"; title = "Request Rate by Service"
            gridPos = @{ h = 8; w = 12; x = 0; y = 5 }
            datasource = @{ type = "prometheus"; uid = $PROM_UID }
            targets = @(@{ refId = "A"; expr = "sum(rate(http_requests_total[5m])) by (job)"; legendFormat = "{{job}}" })
            fieldConfig = @{ defaults = @{ unit = "reqps" } }
            options = @{ tooltip = @{ mode = "multi" } }
        }

        # Time Series: Request Rate by Status Code
        @{
            id = 25; type = "timeseries"; title = "Request Rate by Status Code"
            gridPos = @{ h = 8; w = 12; x = 12; y = 5 }
            datasource = @{ type = "prometheus"; uid = $PROM_UID }
            targets = @(@{ refId = "A"; expr = "sum(rate(http_requests_total[5m])) by (status)"; legendFormat = "HTTP {{status}}" })
            fieldConfig = @{ defaults = @{ unit = "reqps" }; overrides = @(
                @{ matcher = @{ id = "byRegexp"; options = "/5../" }; properties = @(@{ id = "color"; value = @{ mode = "fixed"; fixedColor = "red" } }) }
                @{ matcher = @{ id = "byRegexp"; options = "/4../" }; properties = @(@{ id = "color"; value = @{ mode = "fixed"; fixedColor = "orange" } }) }
            )}
            options = @{ tooltip = @{ mode = "multi" } }
        }

        # Row: Latency
        @{ id = 201; type = "row"; title = "Latency Distribution"; gridPos = @{ h = 1; w = 24; x = 0; y = 13 }; collapsed = $false }

        # Time Series: Latency Percentiles
        @{
            id = 26; type = "timeseries"; title = "Latency Percentiles (P50 / P90 / P99)"
            gridPos = @{ h = 8; w = 12; x = 0; y = 14 }
            datasource = @{ type = "prometheus"; uid = $PROM_UID }
            targets = @(
                @{ refId = "A"; expr = "histogram_quantile(0.50, sum(rate(http_request_duration_seconds_bucket[5m])) by (le))"; legendFormat = "P50" }
                @{ refId = "B"; expr = "histogram_quantile(0.90, sum(rate(http_request_duration_seconds_bucket[5m])) by (le))"; legendFormat = "P90" }
                @{ refId = "C"; expr = "histogram_quantile(0.99, sum(rate(http_request_duration_seconds_bucket[5m])) by (le))"; legendFormat = "P99" }
            )
            fieldConfig = @{ defaults = @{ unit = "s" } }
            options = @{ tooltip = @{ mode = "multi" } }
        }

        # Time Series: Latency per Service
        @{
            id = 27; type = "timeseries"; title = "P95 Latency per Service"
            gridPos = @{ h = 8; w = 12; x = 12; y = 14 }
            datasource = @{ type = "prometheus"; uid = $PROM_UID }
            targets = @(@{ refId = "A"; expr = "histogram_quantile(0.95, sum(rate(http_request_duration_seconds_bucket[5m])) by (le, job))"; legendFormat = "{{job}}" })
            fieldConfig = @{ defaults = @{ unit = "s" } }
            options = @{ tooltip = @{ mode = "multi" } }
        }

        # Row: Business Metrics
        @{ id = 202; type = "row"; title = "Business Metrics"; gridPos = @{ h = 1; w = 24; x = 0; y = 22 }; collapsed = $false }

        # Time Series: Orders
        @{
            id = 28; type = "timeseries"; title = "Orders Rate"
            gridPos = @{ h = 7; w = 8; x = 0; y = 23 }
            datasource = @{ type = "prometheus"; uid = $PROM_UID }
            targets = @(
                @{ refId = "A"; expr = "sum(rate(orders_total[5m]))"; legendFormat = "orders/s" }
            )
            fieldConfig = @{ defaults = @{ unit = "ops" } }
        }

        # Time Series: Payments
        @{
            id = 29; type = "timeseries"; title = "Payments Rate"
            gridPos = @{ h = 7; w = 8; x = 8; y = 23 }
            datasource = @{ type = "prometheus"; uid = $PROM_UID }
            targets = @(
                @{ refId = "A"; expr = "sum(rate(payments_total[5m]))"; legendFormat = "payments/s" }
            )
            fieldConfig = @{ defaults = @{ unit = "ops" } }
        }

        # Gauge: Inventory Levels
        @{
            id = 30; type = "gauge"; title = "Current Inventory Levels"
            gridPos = @{ h = 7; w = 8; x = 16; y = 23 }
            datasource = @{ type = "prometheus"; uid = $PROM_UID }
            targets = @(@{ refId = "A"; expr = "inventory_level"; legendFormat = "{{instance}}" })
            fieldConfig = @{
                defaults = @{
                    min = 0; max = 200
                    thresholds = @{ mode = "absolute"; steps = @(
                        @{ color = "red"; value = $null }
                        @{ color = "orange"; value = 20 }
                        @{ color = "green"; value = 50 }
                    )}
                }
            }
        }
    )
}

New-Dashboard $appDashboard

# ============================================================
# Dashboard 3: Application Logs (Loki)
# ============================================================
Write-Host "`n[3/4] Creating 'Application Logs' dashboard..." -ForegroundColor Cyan

$logsDashboard = @{
    uid = "app-logs"
    title = "Application Logs"
    tags = @("logs", "loki")
    timezone = "browser"
    refresh = "10s"
    time = @{ from = "now-30m"; to = "now" }
    templating = @{
        list = @(
            @{
                name = "namespace"
                type = "custom"
                query = "demo-app,observability,default,kube-system"
                current = @{ text = "demo-app"; value = "demo-app"; selected = $true }
                options = @(
                    @{ text = "demo-app"; value = "demo-app"; selected = $true }
                    @{ text = "observability"; value = "observability"; selected = $false }
                    @{ text = "default"; value = "default"; selected = $false }
                    @{ text = "kube-system"; value = "kube-system"; selected = $false }
                )
            }
            @{
                name = "search"
                type = "textbox"
                query = ""
                current = @{ text = ""; value = "" }
                label = "Log Search"
            }
        )
    }
    panels = @(
        # Row
        @{ id = 300; type = "row"; title = "Log Volume"; gridPos = @{ h = 1; w = 24; x = 0; y = 0 }; collapsed = $false }

        # Time Series: Log volume by namespace
        @{
            id = 31; type = "timeseries"; title = "Log Volume by Namespace"
            gridPos = @{ h = 6; w = 24; x = 0; y = 1 }
            datasource = @{ type = "loki"; uid = $LOKI_UID }
            targets = @(@{
                refId = "A"
                expr = "sum(count_over_time({namespace=~`".+`"}[1m])) by (namespace)"
                legendFormat = "{{namespace}}"
            })
            fieldConfig = @{ defaults = @{ unit = "short" } }
            options = @{
                tooltip = @{ mode = "multi" }
            }
        }

        # Row
        @{ id = 301; type = "row"; title = "Log Stream"; gridPos = @{ h = 1; w = 24; x = 0; y = 7 }; collapsed = $false }

        # Logs Panel: All logs for selected namespace
        @{
            id = 32; type = "logs"; title = "Logs - `$namespace"
            gridPos = @{ h = 16; w = 24; x = 0; y = 8 }
            datasource = @{ type = "loki"; uid = $LOKI_UID }
            targets = @(@{
                refId = "A"
                expr = "{namespace=`"`$namespace`"} |~ `"`$search`""
            })
            options = @{
                showTime = $true
                showLabels = $true
                showCommonLabels = $false
                wrapLogMessage = $true
                prettifyLogMessage = $false
                enableLogDetails = $true
                sortOrder = "Descending"
                dedupStrategy = "none"
            }
        }

        # Row
        @{ id = 302; type = "row"; title = "Error Logs"; gridPos = @{ h = 1; w = 24; x = 0; y = 24 }; collapsed = $false }

        # Logs Panel: Error logs only
        @{
            id = 33; type = "logs"; title = "Error Logs - `$namespace"
            gridPos = @{ h = 12; w = 24; x = 0; y = 25 }
            datasource = @{ type = "loki"; uid = $LOKI_UID }
            targets = @(@{
                refId = "A"
                expr = "{namespace=`"`$namespace`"} |~ `"(?i)error|exception|fail|panic|critical`""
            })
            options = @{
                showTime = $true
                showLabels = $true
                wrapLogMessage = $true
                enableLogDetails = $true
                sortOrder = "Descending"
            }
        }

        # Time Series: Error log rate
        @{
            id = 34; type = "timeseries"; title = "Error Log Rate"
            gridPos = @{ h = 7; w = 24; x = 0; y = 37 }
            datasource = @{ type = "loki"; uid = $LOKI_UID }
            targets = @(@{
                refId = "A"
                expr = "sum(count_over_time({namespace=`"`$namespace`"} |~ `"(?i)error|exception|fail`"[1m])) by (pod)"
                legendFormat = "{{pod}}"
            })
            fieldConfig = @{
                defaults = @{
                    custom = @{ fillOpacity = 20; stacking = @{ mode = "normal" } }
                }
            }
        }
    )
}

New-Dashboard $logsDashboard

# ============================================================
# Dashboard 4: Service Health & SRE Golden Signals
# ============================================================
Write-Host "`n[4/4] Creating 'SRE Golden Signals' dashboard..." -ForegroundColor Cyan

$sreDashboard = @{
    uid = "sre-golden-signals"
    title = "SRE Golden Signals"
    tags = @("sre", "golden-signals", "reliability")
    timezone = "browser"
    refresh = "15s"
    time = @{ from = "now-1h"; to = "now" }
    annotations = @{
        list = @(@{
            name = "Alerts"
            datasource = @{ type = "prometheus"; uid = $PROM_UID }
            enable = $true
            expr = "ALERTS{alertstate=`"firing`"}"
            titleFormat = "{{alertname}}"
            textFormat = "{{description}}"
            iconColor = "red"
        })
    }
    panels = @(
        # Row: Traffic
        @{ id = 400; type = "row"; title = "Traffic (Request Rate)"; gridPos = @{ h = 1; w = 24; x = 0; y = 0 }; collapsed = $false }

        @{
            id = 40; type = "timeseries"; title = "Request Rate (QPS) by Service"
            gridPos = @{ h = 8; w = 12; x = 0; y = 1 }
            datasource = @{ type = "prometheus"; uid = $PROM_UID }
            targets = @(@{ refId = "A"; expr = "sum(rate(http_requests_total[5m])) by (job)"; legendFormat = "{{job}}" })
            fieldConfig = @{ defaults = @{ unit = "reqps"; custom = @{ fillOpacity = 15 } } }
            options = @{ tooltip = @{ mode = "multi" } }
        }

        @{
            id = 41; type = "timeseries"; title = "Request Rate by Method & Endpoint"
            gridPos = @{ h = 8; w = 12; x = 12; y = 1 }
            datasource = @{ type = "prometheus"; uid = $PROM_UID }
            targets = @(@{ refId = "A"; expr = "sum(rate(http_requests_total[5m])) by (method, endpoint)"; legendFormat = "{{method}} {{endpoint}}" })
            fieldConfig = @{ defaults = @{ unit = "reqps" } }
            options = @{ tooltip = @{ mode = "multi" } }
        }

        # Row: Errors
        @{ id = 401; type = "row"; title = "Errors (Error Rate)"; gridPos = @{ h = 1; w = 24; x = 0; y = 9 }; collapsed = $false }

        @{
            id = 42; type = "timeseries"; title = "Error Rate % by Service"
            gridPos = @{ h = 8; w = 12; x = 0; y = 10 }
            datasource = @{ type = "prometheus"; uid = $PROM_UID }
            targets = @(@{
                refId = "A"
                expr = "sum(rate(http_requests_total{status=~`"5..`"}[5m])) by (job) / sum(rate(http_requests_total[5m])) by (job) * 100"
                legendFormat = "{{job}}"
            })
            fieldConfig = @{ defaults = @{ unit = "percent"; min = 0; custom = @{ fillOpacity = 20 } }; overrides = @() }
            options = @{ tooltip = @{ mode = "multi" } }
        }

        @{
            id = 43; type = "timeseries"; title = "5xx Errors per Service"
            gridPos = @{ h = 8; w = 12; x = 12; y = 10 }
            datasource = @{ type = "prometheus"; uid = $PROM_UID }
            targets = @(@{
                refId = "A"
                expr = "sum(rate(http_requests_total{status=~`"5..`"}[5m])) by (job)"
                legendFormat = "{{job}}"
            })
            fieldConfig = @{ defaults = @{ unit = "reqps"; custom = @{ fillOpacity = 30; lineStyle = @{ fill = "solid" } } } }
        }

        # Row: Latency
        @{ id = 402; type = "row"; title = "Latency (Duration)"; gridPos = @{ h = 1; w = 24; x = 0; y = 18 }; collapsed = $false }

        @{
            id = 44; type = "timeseries"; title = "Latency P50 / P90 / P99"
            gridPos = @{ h = 8; w = 12; x = 0; y = 19 }
            datasource = @{ type = "prometheus"; uid = $PROM_UID }
            targets = @(
                @{ refId = "A"; expr = "histogram_quantile(0.50, sum(rate(http_request_duration_seconds_bucket[5m])) by (le))"; legendFormat = "P50" }
                @{ refId = "B"; expr = "histogram_quantile(0.90, sum(rate(http_request_duration_seconds_bucket[5m])) by (le))"; legendFormat = "P90" }
                @{ refId = "C"; expr = "histogram_quantile(0.99, sum(rate(http_request_duration_seconds_bucket[5m])) by (le))"; legendFormat = "P99" }
            )
            fieldConfig = @{ defaults = @{ unit = "s"; custom = @{ fillOpacity = 10 } } }
            options = @{ tooltip = @{ mode = "multi" } }
        }

        @{
            id = 45; type = "timeseries"; title = "P95 Latency per Service"
            gridPos = @{ h = 8; w = 12; x = 12; y = 19 }
            datasource = @{ type = "prometheus"; uid = $PROM_UID }
            targets = @(@{ refId = "A"; expr = "histogram_quantile(0.95, sum(rate(http_request_duration_seconds_bucket[5m])) by (le, job))"; legendFormat = "{{job}}" })
            fieldConfig = @{ defaults = @{ unit = "s" } }
            options = @{ tooltip = @{ mode = "multi" } }
        }

        # Row: Saturation
        @{ id = 403; type = "row"; title = "Saturation (Resource Usage)"; gridPos = @{ h = 1; w = 24; x = 0; y = 27 }; collapsed = $false }

        @{
            id = 46; type = "timeseries"; title = "Process CPU Seconds Rate"
            gridPos = @{ h = 7; w = 8; x = 0; y = 28 }
            datasource = @{ type = "prometheus"; uid = $PROM_UID }
            targets = @(@{ refId = "A"; expr = "rate(process_cpu_seconds_total[5m])"; legendFormat = "{{job}}" })
            fieldConfig = @{ defaults = @{ unit = "percentunit" } }
        }

        @{
            id = 47; type = "timeseries"; title = "Process Resident Memory"
            gridPos = @{ h = 7; w = 8; x = 8; y = 28 }
            datasource = @{ type = "prometheus"; uid = $PROM_UID }
            targets = @(@{ refId = "A"; expr = "process_resident_memory_bytes"; legendFormat = "{{job}}" })
            fieldConfig = @{ defaults = @{ unit = "bytes" } }
        }

        @{
            id = 48; type = "timeseries"; title = "Open File Descriptors"
            gridPos = @{ h = 7; w = 8; x = 16; y = 28 }
            datasource = @{ type = "prometheus"; uid = $PROM_UID }
            targets = @(
                @{ refId = "A"; expr = "process_open_fds"; legendFormat = "{{job}} open" }
                @{ refId = "B"; expr = "process_max_fds"; legendFormat = "{{job}} max" }
            )
            fieldConfig = @{ defaults = @{ unit = "short" } }
        }
    )
}

New-Dashboard $sreDashboard

Write-Host "`n✅ All dashboards created! Access at: http://localhost:30300/dashboards" -ForegroundColor Green
Write-Host "  - Kubernetes Cluster Overview:  http://localhost:30300/d/k8s-cluster-overview" -ForegroundColor White
Write-Host "  - Demo App Service Metrics:     http://localhost:30300/d/demo-app-metrics" -ForegroundColor White
Write-Host "  - Application Logs:             http://localhost:30300/d/app-logs" -ForegroundColor White
Write-Host "  - SRE Golden Signals:           http://localhost:30300/d/sre-golden-signals" -ForegroundColor White
