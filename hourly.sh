#!/bin/bash

# Hourly Crypto Stats Script
# Shows Low (last hour), Last, High (last hour), Variance for each coin

CSV_FILE="/home/trading/sisu/prices.csv"

# Check if CSV file exists
if [ ! -f "$CSV_FILE" ]; then
    echo "Error: CSV file not found at $CSV_FILE"
    exit 1
fi

# Get current time and one hour ago
CURRENT_TIME=$(date '+%Y-%m-%d %H:%M:%S')
ONE_HOUR_AGO=$(date -d '1 hour ago' '+%Y-%m-%d %H:%M:%S')

echo "==========================================================="
echo "    Hourly Crypto     >>-----<<     $(date '+%I:%M:%S%p')"
echo "==========================================================="
echo " Coin | Low (1h)    |    Last     | High (1h)   | Variance"
echo "------|-------------|-------------|-------------|----------"

# Define coins in same order as Program.cs
COINS=("BTC" "ETH" "XRP" "LINK" "ALGO" "BAT")

for coin in "${COINS[@]}"; do
    # Get data from last hour for this coin
    HOUR_DATA=$(awk -F',' -v coin="$coin" -v start_time="$ONE_HOUR_AGO" '
        NR > 1 && $2 == coin && $1 >= start_time {
            print $3 "," $4 "," $5  # low,last,high
        }' "$CSV_FILE")
    
    if [ -z "$HOUR_DATA" ]; then
        echo "${coin}     | No data     | No data     | No data     | No data"
        continue
    fi
    
    # Calculate stats using awk
    STATS=$(echo "$HOUR_DATA" | awk -F',' '
        BEGIN {
            min_low = 999999999
            max_high = 0
            last_price = 0
        }
        {
            if ($1 < min_low) min_low = $1
            if ($3 > max_high) max_high = $3
            last_price = $2  # Keep updating to get the most recent
        }
        END {
            if (NR > 0) {
                avg = (min_low + max_high) / 2
                variance = last_price - avg
                printf "%.2f,%.2f,%.2f,%.2f", min_low, last_price, max_high, variance
            }
        }')
    
    if [ -n "$STATS" ]; then
        # Parse the stats
        LOW=$(echo "$STATS" | cut -d',' -f1)
        LAST=$(echo "$STATS" | cut -d',' -f2)
        HIGH=$(echo "$STATS" | cut -d',' -f3)
        VARIANCE=$(echo "$STATS" | cut -d',' -f4)
        
        # Format and display
        printf "%-5s | \$%10.2f | \$%10.2f | \$%10.2f | \$%8.2f\n" "$coin" "$LOW" "$LAST" "$HIGH" "$VARIANCE"
    else
        echo "${coin}     | Error       | Error       | Error       | Error"
    fi
done

echo "==============================================="
echo "Data from: $(date -d '1 hour ago' '+%I:%M:%S%p') to $(date '+%I:%M:%S%p')"