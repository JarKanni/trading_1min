tmux has-session -t trading 2>/dev/null && tmux attach-session -t trading || tmux new-session -s trading -c /home/trading/sisu 'dotnet run -c Release'
